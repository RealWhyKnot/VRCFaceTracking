using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.mDNS;
using VRCFaceTracking.Core.Models.ParameterDefinition;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.OSC.Query.mDNS;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Core.Services;

public partial class OscQueryService(
    ILogger<OscQueryService> logger,
    OscQueryConfigParser oscQueryConfigParser,
    AvatarConfigParser avatarConfigParser,
    ParameterSenderService parameterSenderService,
    IOscTarget oscTarget,
    MulticastDnsService multicastDnsService,
    OscRecvService recvService,
    ILocalSettingsService settingsService,
    HttpHandler httpHandler,
    IDispatcherService dispatcherService)
    : ObservableObject, IHostedService
{
    private static readonly TimeSpan AvatarPollInterval = TimeSpan.FromSeconds(1);

    [ObservableProperty] private IAvatarInfo _avatarInfo = new NullAvatarDef("Loading...", "Loading...");
    [ObservableProperty] private List<Parameter> _avatarParameters;

    private readonly CancellationTokenSource _stopAvatarPoll = new();
    private Task _avatarPoll = Task.CompletedTask;

    public async Task InitializeAsync()
    {
        logger.LogDebug("OSC Service Initializing");

        recvService.OnMessageReceived += HandleNewMessage;
        recvService.HandleAddress("/avatar/change");
        recvService.HandleAddress("/vrcft/settings/forceRelevant");

        await settingsService.Load(oscTarget);

        (bool listenerSuccess, bool senderSuccess) result = (false, false);

        if (string.IsNullOrWhiteSpace(oscTarget.DestinationAddress))
        {
            return;  // Return both false as we cant bind to anything without an address
        }

        multicastDnsService.OnVrcClientDiscovered += FirstClientDiscovered;

        multicastDnsService.SendQuery("_oscjson._tcp.local");

        logger.LogDebug("OSC Service Initialized with result {0}", result);
        await Task.CompletedTask;
    }

    private int _clientDiscovered;

    private void FirstClientDiscovered()
    {
        if (Interlocked.Exchange(ref _clientDiscovered, 1) == 1)
        {
            return;
        }

        multicastDnsService.OnVrcClientDiscovered -= FirstClientDiscovered;

        logger.LogInformation($"OSCQuery detected at {multicastDnsService.VrchatClientEndpoint}. Setting port negotiation to autopilot.");

        httpHandler.OnHostInfoQueried += HandleNewAvatarWrapper;

        var recvEndpoint = recvService.UpdateTarget(new IPEndPoint(IPAddress.Parse(oscTarget.DestinationAddress), 0));
        if (recvEndpoint == null)
        {
            logger.LogError("Very strange. We were unable to bind to a random port.");
            recvEndpoint = new IPEndPoint(IPAddress.Parse(oscTarget.DestinationAddress), oscTarget.InPort);
        }

        var randomServiceSuffix = Utils.GetRandomChars(6);
        var httpPort = Utils.GetRandomFreePort();
        httpHandler.SetAppName("VRCFT-" + randomServiceSuffix);
        httpHandler.BindTo($"http://127.0.0.1:{httpPort}/", recvEndpoint.Port);

        // Advertise our OSC JSON and OSC endpoints (OSC JSON to display the silly lil popup in-game)
        multicastDnsService.Advertise("_oscjson._tcp", new AdvertisedService("VRCFT-" + randomServiceSuffix, httpPort, IPAddress.Loopback));
        multicastDnsService.Advertise("_osc._udp", new AdvertisedService("VRCFT-" + randomServiceSuffix, recvEndpoint.Port, IPAddress.Loopback));

        HandleNewAvatar();
    }

    private readonly SemaphoreSlim _avatarParseLock = new(1, 1);
    private string _loadedAvatarId;

    private async void HandleNewAvatar(string newId = null)
    {
        if (newId == null)
        {
            if (!await _avatarParseLock.WaitAsync(0))
            {
                return;
            }
        }
        else
        {
            await _avatarParseLock.WaitAsync();
        }

        try
        {
            if (newId != null && string.Equals(newId, _loadedAvatarId, StringComparison.Ordinal))
            {
                logger.LogDebug("diag.avatar skipping reload, {Id} is already loaded", newId);
                return;
            }

            (IAvatarInfo avatarInfo, List<Parameter> relevantParameters)? newAvatar;
            if (multicastDnsService.VrchatClientEndpoint != null)
            {
                newAvatar = await oscQueryConfigParser.ParseAvatar("");
            }
            else
            {
                // handle normal osc
                newAvatar = await avatarConfigParser.ParseAvatar(newId);
            }

            if (!newAvatar.HasValue)
            {
                return;
            }

            // Parsing success. Deregister callback and update values
            httpHandler.OnHostInfoQueried -= HandleNewAvatarWrapper;
            _loadedAvatarId = newAvatar.Value.avatarInfo.Id;
            dispatcherService.Run(() =>
            {
                AvatarInfo = newAvatar.Value.avatarInfo;
                AvatarParameters = newAvatar.Value.relevantParameters;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load the avatar configuration");
        }
        finally
        {
            _avatarParseLock.Release();
        }
    }

    private void HandleNewAvatarWrapper() => HandleNewAvatar(); // Helper func used in callbacks

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _avatarPoll = PollAvatarId(
            () => multicastDnsService.VrchatClientEndpoint,
            oscQueryConfigParser.GetAvatarId,
            () => _loadedAvatarId,
            HandleNewAvatar,
            AvatarPollInterval,
            logger,
            _stopAvatarPoll.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopAvatarPoll.Cancel();
        return _avatarPoll;
    }

    internal static async Task PollAvatarId(
        Func<IPEndPoint?> endpoint,
        Func<IPEndPoint, CancellationToken, Task<string?>> fetchId,
        Func<string?> loadedId,
        Action<string> load,
        TimeSpan interval,
        ILogger logger,
        CancellationToken ct)
    {
        var failing = false;
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (endpoint() is not { } target)
                {
                    continue;
                }

                try
                {
                    var id = await fetchId(target, ct);
                    if (failing)
                    {
                        failing = false;
                        logger.LogDebug("diag.avatar poll of {Endpoint} recovered", target);
                    }

                    if (id != null && id != loadedId())
                    {
                        load(id);
                    }
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    if (!failing)
                    {
                        failing = true;
                        logger.LogDebug("diag.avatar poll of {Endpoint} failed: {Message}", target, e.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }


    private void HandleNewMessage(OscMessage msg)
    {
        switch (msg.Address)
        {
            case "/avatar/change":
                HandleNewAvatar(msg.Value as string);
                break;
            case "/vrcft/settings/forceRelevant":   // Endpoint for external tools to force vrcft to send all parameters
                if (msg.Value is bool relevancy)
                {
                    parameterSenderService.AllParametersRelevant = relevancy;
                }

                break;
                /*
                case "/avatar/parameters/EyeTrackingActive":
                    if (UnifiedLibManager.EyeStatus != ModuleState.Uninitialized)
                    {
                        if (!msg.Value.BoolValue)
                            UnifiedLibManager.EyeStatus = ModuleState.Idle;
                        else UnifiedLibManager.EyeStatus = ModuleState.Active;
                    }
                    break;
                case "/avatar/parameters/LipTrackingActive":
                case "/avatar/parameters/ExpressionTrackingActive":
                    {
                        if (!msg.Value.BoolValue)
                    if (UnifiedLibManager.ExpressionStatus != ModuleState.Uninitialized)
                            UnifiedLibManager.ExpressionStatus = ModuleState.Idle;
                        else UnifiedLibManager.ExpressionStatus = ModuleState.Active;
                    }
                    break;
                */
        }
    }

    partial void OnAvatarParametersChanged(List<Parameter> value)
    {
        logger.LogDebug("Configuration loaded. Checking for native tracking parameters...");
        var hasLoadedNative = value.Any(p => p.GetParamNames().Any(t => t.paramName.StartsWith("/tracking/")));
        if (hasLoadedNative)
        {
            logger.LogWarning("Native tracking parameters detected.");
        }

        var deprecatedParams = value.Where(p => p.Deprecated).ToList();

        logger.LogInformation(value.Count + " parameters loaded.");
        if (deprecatedParams.Any())
        {
            logger.LogWarning(
                deprecatedParams.Count +
                " Legacy parameters detected. " +
                "Please consider updating the avatar to use the latest documented parameters.");
            logger.LogDebug($"Loaded deprecated parameters: {string.Join(", ", deprecatedParams.SelectMany(x => x.GetParamNames()).Select(y => y.paramName))}");
        }
    }
}
