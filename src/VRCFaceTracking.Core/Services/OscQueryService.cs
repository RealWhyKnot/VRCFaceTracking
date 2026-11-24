using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Mdns;
using VRCFaceTracking.Core.Models.ParameterDefinition;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Osc.Query;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Core.Services;

public sealed partial class OscQueryService : ObservableObject, IHostedService, IDisposable
{
    private static readonly TimeSpan AvatarPollInterval = TimeSpan.FromSeconds(1);
    internal const int MaxPollBackoffTicks = 8;
    internal const string FullFaceTrackingAddress = "/avatar/avatarfullfacetracking";
    internal const string VoiceAddress = "/avatar/parameters/Voice";
    internal const string MuteSelfAddress = "/avatar/parameters/MuteSelf";

    private readonly ILogger<OscQueryService> _logger;
    private readonly OscQueryConfigParser _oscQueryConfigParser;
    private readonly AvatarConfigParser _avatarConfigParser;
    private readonly IOscTarget _oscTarget;
    private readonly MulticastDnsService _multicastDnsService;
    private readonly OscRecvService _recvService;
    private readonly ILocalSettingsService _settingsService;
    private readonly HttpHandler _httpHandler;
    private readonly IDispatcherService _dispatcherService;
    private readonly BinaryFaceDataSender _binaryFaceDataSender;
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _avatarParseLock = new(1, 1);
    private readonly AvatarPoll _avatarPoll;

    [ObservableProperty] private IAvatarInfo _avatarInfo = new NullAvatarDef("Loading...", "Loading...");
    [ObservableProperty] private List<Parameter>? _avatarParameters;

    private int _clientDiscovered;
    private string? _loadedAvatarId;
    private bool _loadedFullFaceTracking;
    private string? _requestedAvatarId;
    private string? _seenAvatarId;
    private bool? _reportedFullFaceTracking;

    public OscQueryService(
        ILogger<OscQueryService> logger,
        OscQueryConfigParser oscQueryConfigParser,
        AvatarConfigParser avatarConfigParser,
        IOscTarget oscTarget,
        MulticastDnsService multicastDnsService,
        OscRecvService recvService,
        ILocalSettingsService settingsService,
        HttpHandler httpHandler,
        IDispatcherService dispatcherService,
        BinaryFaceDataSender binaryFaceDataSender)
    {
        _logger = logger;
        _oscQueryConfigParser = oscQueryConfigParser;
        _avatarConfigParser = avatarConfigParser;
        _oscTarget = oscTarget;
        _multicastDnsService = multicastDnsService;
        _recvService = recvService;
        _settingsService = settingsService;
        _httpHandler = httpHandler;
        _dispatcherService = dispatcherService;
        _binaryFaceDataSender = binaryFaceDataSender;
        _avatarPoll = new AvatarPoll(this);
    }

    public void Dispose()
    {
        _avatarPoll.Dispose();
        _stopping.Dispose();
        _avatarParseLock.Dispose();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        LogOscServiceInitializing(_logger);

        _recvService.OnMessageReceived += HandleNewMessage;
        _recvService.HandleAddress("/avatar/change");
        _recvService.HandleAddress(FullFaceTrackingAddress);
        _recvService.HandleAddress("/vrcft/settings/forceRelevant");
        _recvService.HandleAddress(VoiceAddress);
        _recvService.HandleAddress(MuteSelfAddress);

        await _settingsService.LoadAsync(_oscTarget, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(_oscTarget.DestinationAddress))
        {
            return;
        }

        _multicastDnsService.VrcClientDiscovered += FirstClientDiscovered;

        await _multicastDnsService.SendQueryAsync("_oscjson._tcp.local", cancellationToken).ConfigureAwait(false);

        LogOscServiceInitialized(_logger);
    }

    private async void FirstClientDiscovered(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _clientDiscovered, 1) == 1)
        {
            return;
        }

        _multicastDnsService.VrcClientDiscovered -= FirstClientDiscovered;

        LogOSCQueryDetectedSettingPortNegotiation(_logger, _multicastDnsService.VrchatClientEndpoint);

        _httpHandler.HostInfoQueried += HandleNewAvatarWrapper;

        if (!IPAddress.TryParse(_oscTarget.DestinationAddress, out IPAddress? destination))
        {
            LogOscAddressNotAnIp(_logger, _oscTarget.DestinationAddress);
            destination = IPAddress.Loopback;
        }

        IPEndPoint? recvEndpoint = _recvService.UpdateTarget(new IPEndPoint(destination, 0));
        if (recvEndpoint == null)
        {
            LogVeryStrangeWereUnableBind(_logger);
            recvEndpoint = new IPEndPoint(destination, _oscTarget.InPort);
        }

        string randomServiceSuffix = Utils.GetRandomChars(6);
        int httpPort = Utils.GetRandomFreePort();
        _httpHandler.SetAppName("VRCFT-" + randomServiceSuffix);
        _httpHandler.BindTo(new Uri($"http://127.0.0.1:{httpPort}/"), recvEndpoint.Port);

        _multicastDnsService.Advertise("_oscjson._tcp", new AdvertisedService("VRCFT-" + randomServiceSuffix, httpPort, IPAddress.Loopback));
        _multicastDnsService.Advertise("_osc._udp", new AdvertisedService("VRCFT-" + randomServiceSuffix, recvEndpoint.Port, recvEndpoint.Address));

        await HandleNewAvatarAsync(newId: null, _stopping.Token).ConfigureAwait(false);
    }

    private async Task<bool> HandleNewAvatarAsync(string? newId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await TryEnterAvatarParseAsync(newId, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        try
        {
            bool? reportedFullFaceTracking = _reportedFullFaceTracking;
            if (IsAlreadyLoaded(newId, _loadedAvatarId, reportedFullFaceTracking, _loadedFullFaceTracking))
            {
                LogDiagAvatarSkippingReloadAlready(_logger, newId);
                return true;
            }

            OscQueryLoadResult result = await LoadConfigAsync(newId, reportedFullFaceTracking, cancellationToken).ConfigureAwait(false);
            (IAvatarInfo avatarInfo, List<Parameter> relevantParameters)? newAvatar = result.Avatar;
            if (!newAvatar.HasValue)
            {
                return !result.NotFound;
            }

            _httpHandler.HostInfoQueried -= HandleNewAvatarWrapper;
            _loadedAvatarId = newAvatar.Value.avatarInfo.Id;
            _loadedFullFaceTracking = newAvatar.Value.avatarInfo.FullFaceTracking;
            _binaryFaceDataSender.SetAvatar(newAvatar.Value.avatarInfo);
            _dispatcherService.Run(() =>
            {
                AvatarInfo = newAvatar.Value.avatarInfo;
                AvatarParameters = newAvatar.Value.relevantParameters;
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailedLoadAvatarConfiguration(_logger, ex);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
        finally
        {
            _avatarParseLock.Release();
        }
    }

    private async Task<OscQueryLoadResult> LoadConfigAsync(string? newId, bool? reportedFullFaceTracking, CancellationToken cancellationToken)
    {
        return _multicastDnsService.VrchatClientEndpoint != null
            ? await _oscQueryConfigParser.LoadAvatarAsync(cancellationToken).ConfigureAwait(false)
            : new OscQueryLoadResult(
                Avatar: await _avatarConfigParser.ParseAvatarAsync(newId, skipParams: false, fullFaceTracking: reportedFullFaceTracking == true, cancellationToken).ConfigureAwait(false),
                NotFound: false);
    }

    private async Task<bool> TryEnterAvatarParseAsync(string? newId, CancellationToken cancellationToken)
    {
        if (newId == null)
        {
            return await _avatarParseLock.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }

        await _avatarParseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal static bool IsAlreadyLoaded(string? newId, string? loadedId, bool? reportedFullFaceTracking, bool loadedFullFaceTracking) =>
        newId != null && string.Equals(newId, loadedId, StringComparison.Ordinal)
        && (reportedFullFaceTracking == null || reportedFullFaceTracking == loadedFullFaceTracking);

    private async void HandleNewAvatarWrapper(object? sender, EventArgs e) =>
        await HandleNewAvatarAsync(newId: null, _stopping.Token).ConfigureAwait(false);

    public Task StartAsync(CancellationToken cancellationToken) => _avatarPoll.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _avatarPoll.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static int PollBackoffTicks(int consecutiveNotFound) =>
        consecutiveNotFound < 1 ? 0 : Math.Min((1 << Math.Min(consecutiveNotFound, 4)) - 1, MaxPollBackoffTicks);

    private void NoteAvatarId(string? id)
    {
        if (id != null && !string.Equals(Interlocked.Exchange(ref _seenAvatarId, id), id, StringComparison.Ordinal))
        {
            _oscQueryConfigParser.NotifyAvatarChanged();
        }
    }

    internal static async Task PollAvatarIdAsync(
        Func<IPEndPoint?> endpoint,
        Func<IPEndPoint, CancellationToken, Task<string?>> fetchId,
        Func<string?> loadedId,
        Func<string, Task<bool>> load,
        TimeSpan interval,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        bool failing = false;
        int notFound = 0;
        int skip = 0;
        string? lastId = null;
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (endpoint() is not { } target)
                {
                    continue;
                }

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                try
                {
                    string? id = await fetchId(target, cancellationToken).ConfigureAwait(false);
                    if (failing)
                    {
                        failing = false;
                        LogDiagAvatarPollRecovered(logger, target);
                    }

                    bool pending = id != null && !string.Equals(id, loadedId(), StringComparison.Ordinal);
                    if (!pending || !string.Equals(id, lastId, StringComparison.Ordinal))
                    {
                        notFound = 0;
                    }

                    lastId = id;
                    if (pending)
                    {
                        notFound = await load(id!).ConfigureAwait(false) ? 0 : notFound + 1;
                        skip = PollBackoffTicks(notFound);
                    }
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or JsonException)
                {
                    notFound = e is HttpRequestException { StatusCode: HttpStatusCode.NotFound } ? notFound + 1 : 0;
                    skip = PollBackoffTicks(notFound);
                    if (!failing)
                    {
                        failing = true;
                        LogDiagAvatarPollFailed(logger, target, e.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void HandleNewMessage(OscMessage msg)
    {
        switch (msg.Address)
        {
            case "/avatar/change":
                _requestedAvatarId = msg.Value as string;
                NoteAvatarId(_requestedAvatarId);
                _reportedFullFaceTracking = null;
                _ = HandleNewAvatarAsync(_requestedAvatarId, _stopping.Token);
                break;
            case FullFaceTrackingAddress:
                if (msg.Value is bool fullFaceTracking)
                {
                    _reportedFullFaceTracking = fullFaceTracking;
                    _ = HandleNewAvatarAsync(_requestedAvatarId ?? _loadedAvatarId, _stopping.Token);
                }

                break;
            case "/vrcft/settings/forceRelevant":
                if (msg.Value is bool relevancy)
                {
                    ParameterSenderService.AllParametersRelevant = relevancy;
                }

                break;
            case VoiceAddress:
                if (msg.Value is float level && VoiceActivity.SetLevel(level))
                {
                    LogVoiceLevelReceived(_logger);
                }

                break;
            case MuteSelfAddress:
                if (msg.Value is bool muted)
                {
                    VoiceActivity.SetMuted(muted);
                }

                break;
            default:
                break;
        }
    }

    private sealed class AvatarPoll : BackgroundService
    {
        private readonly OscQueryService _owner;

        public AvatarPoll(OscQueryService owner)
        {
            _owner = owner;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            PollAvatarIdAsync(
                () => _owner._multicastDnsService.VrchatClientEndpoint,
                _owner._oscQueryConfigParser.GetAvatarIdAsync,
                () => _owner._loadedAvatarId,
                id =>
                {
                    _owner.NoteAvatarId(id);
                    return _owner.HandleNewAvatarAsync(id, stoppingToken);
                },
                AvatarPollInterval,
                _owner._logger,
                stoppingToken);
    }

    partial void OnAvatarParametersChanged(List<Parameter>? value)
    {
        if (value == null)
        {
            return;
        }

        LogConfigurationLoadedCheckingNativeTracking(_logger);
        bool hasLoadedNative = value.Any(p => p.GetParamNames().Any(t => t.paramName.StartsWith("/tracking/", StringComparison.Ordinal)));
        if (hasLoadedNative)
        {
            LogNativeTrackingParametersDetected(_logger);
        }

        List<Parameter> deprecatedParams = [.. value.Where(p => p.Deprecated)];

        LogParametersLoaded(_logger, value.Count);
        if (deprecatedParams.Count > 0)
        {
            LogLegacyParametersDetected(_logger, deprecatedParams.Count);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                LogLoadedDeprecatedParameters(_logger, string.Join(", ", deprecatedParams.SelectMany(x => x.GetParamNames()).Select(y => y.paramName)));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "OSC Service Initializing")]
    private static partial void LogOscServiceInitializing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "OSC Service Initialized")]
    private static partial void LogOscServiceInitialized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "OSCQuery detected at {VrchatClientEndpoint}. Setting port negotiation to autopilot.")]
    private static partial void LogOSCQueryDetectedSettingPortNegotiation(ILogger logger, IPEndPoint? vrchatClientEndpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Very strange. We were unable to bind to a random port.")]
    private static partial void LogVeryStrangeWereUnableBind(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OSC address {Address} isn't an IP address; using 127.0.0.1 for OSCQuery")]
    private static partial void LogOscAddressNotAnIp(ILogger logger, string? address);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.avatar skipping reload, {Id} is already loaded")]
    private static partial void LogDiagAvatarSkippingReloadAlready(ILogger logger, string? id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load the avatar configuration")]
    private static partial void LogFailedLoadAvatarConfiguration(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.avatar poll of {Endpoint} recovered")]
    private static partial void LogDiagAvatarPollRecovered(ILogger logger, IPEndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.avatar poll of {Endpoint} failed: {Message}")]
    private static partial void LogDiagAvatarPollFailed(ILogger logger, IPEndPoint endpoint, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Configuration loaded. Checking for native tracking parameters...")]
    private static partial void LogConfigurationLoadedCheckingNativeTracking(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Native tracking parameters detected.")]
    private static partial void LogNativeTrackingParametersDetected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded deprecated parameters: {Select}")]
    private static partial void LogLoadedDeprecatedParameters(ILogger logger, string select);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} parameters loaded.")]
    private static partial void LogParametersLoaded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} Legacy parameters detected. Please consider updating the avatar to use the latest documented parameters.")]
    private static partial void LogLegacyParametersDetected(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Receiving the voice level from VRChat")]
    private static partial void LogVoiceLevelReceived(ILogger logger);
}
