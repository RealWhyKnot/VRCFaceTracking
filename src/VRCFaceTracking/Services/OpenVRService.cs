using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Valve.VR;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Services;

internal sealed partial class OpenVRService : IDisposable
{
    private static readonly TimeSpan EventPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(30);
    private static readonly uint EventSize = (uint)Marshal.SizeOf<VREvent_t>();

    private readonly ILogger<OpenVRService> _logger;
    private readonly Lock _initLock = new();
    private readonly CancellationTokenSource _serviceCts = new();
    private CVRSystem? _system;
    private CancellationTokenSource? _pollingCts;
    private Task? _reconnectLoop;
    private bool _nativeMissing;
    private bool _disposed;

    public static bool Enabled = true;

    public static bool IsSupported => !OperatingSystem.IsMacOS();

    public event EventHandler? QuitRequested;

    [SavedSetting("ExitWithSteamVR", false)]
    public bool ExitWithSteamVr
    {
        get; set;
    }

    public OpenVRService(ILogger<OpenVRService> logger)
    {
        _logger = logger;
    }

    public bool Initialize(bool quiet = false)
    {
        lock (_initLock)
        {
            if (IsInitialized)
            {
                return true;
            }

            if (!Enabled || !IsSupported || _nativeMissing)
            {
                return false;
            }

            EVRInitError error = EVRInitError.None;
            try
            {
                _system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException)
            {
                _nativeMissing = true;
                LogOpenVRNativeLibraryNotAvailable(_logger, ex.Message);
                return false;
            }

            if (error != EVRInitError.None)
            {
                LogOpenVRInitFailed(_logger, quiet ? LogLevel.Debug : LogLevel.Warning, error);
                _system = null;
                return false;
            }

            string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;
            string fullManifestPath = Path.Combine(currentDirectory, "app.vrmanifest");
            EVRApplicationError manifestRegisterResult = OpenVR.Applications.AddApplicationManifest(fullManifestPath, bTemporary: false);
            if (manifestRegisterResult != EVRApplicationError.None)
            {
                LogManifestRegisterFailed(_logger, quiet ? LogLevel.Debug : LogLevel.Warning, manifestRegisterResult);
                _system = null;
                OpenVR.Shutdown();
                return false;
            }

            LogSuccessfullyInitializedOpenVR(_logger);
            IsInitialized = true;

            if (_pollingCts == null)
            {
                _pollingCts = new CancellationTokenSource();
                _ = PumpEventsAsync(_pollingCts.Token);
            }

            return IsInitialized;
        }
    }

    public void StartReconnectLoop()
    {
        if (!IsSupported || _nativeMissing)
        {
            return;
        }
        _reconnectLoop ??= ReconnectAsync(_serviceCts.Token);
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(ReconnectInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsInitialized)
                {
                    Initialize(quiet: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    public void StopService()
    {
        _serviceCts.Cancel();
        Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopService();
        _serviceCts.Dispose();
    }

    private async Task PumpEventsAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(EventPollInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                CVRSystem? system = _system;
                if (system == null || DrainEvents(system))
                {
                    return;
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
    }

    private bool DrainEvents(CVRSystem system)
    {
        var vrEvent = new VREvent_t();
        while (system.PollNextEvent(ref vrEvent, EventSize))
        {
            if ((EVREventType)vrEvent.eventType != EVREventType.VREvent_Quit)
            {
                continue;
            }

            if (ExitWithSteamVr)
            {
                LogSteamVRShuttingDownClosingVRCFaceTracking(_logger);
                system.AcknowledgeQuit_Exiting();
                Shutdown();
                QuitRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                LogSteamVRShuttingDownReleasingOpenVR(_logger);
                Shutdown();
            }

            return true;
        }

        return false;
    }

    public void Shutdown()
    {
        lock (_initLock)
        {
            if (!IsInitialized)
            {
                return;
            }

            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
            IsInitialized = false;
            _system = null;
            OpenVR.Shutdown();
        }
    }

    public void InitIfNotAlready()
    {
        if (!IsInitialized)
        {
            Initialize();
        }
    }

    public bool IsInitialized
    {
        get; private set;
    }

    public bool AutoStart
    {
        get => IsInitialized && OpenVR.Applications.GetApplicationAutoLaunch("benaclejames.vrcft");
        set
        {
            if (!IsInitialized && !Initialize())
            {
                LogFailedSetAutoStartPreferenceOpenVR(_logger);
                return;
            }

            EVRApplicationError setAutoLaunchResult = OpenVR.Applications.SetApplicationAutoLaunch("benaclejames.vrcft", value);
            if (setAutoLaunchResult != EVRApplicationError.None)
            {
                LogFailedSetAutoLaunch(_logger, setAutoLaunchResult);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenVR native library not available: {Message}")]
    private static partial void LogOpenVRNativeLibraryNotAvailable(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully initialized OpenVR")]
    private static partial void LogSuccessfullyInitializedOpenVR(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SteamVR is shutting down, closing VRCFaceTracking")]
    private static partial void LogSteamVRShuttingDownClosingVRCFaceTracking(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SteamVR is shutting down, releasing OpenVR and staying open")]
    private static partial void LogSteamVRShuttingDownReleasingOpenVR(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to set AutoStart preference. OpenVR couldn't be initialized.")]
    private static partial void LogFailedSetAutoStartPreferenceOpenVR(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to set auto launch: {Error}")]
    private static partial void LogFailedSetAutoLaunch(ILogger logger, EVRApplicationError error);

    [LoggerMessage(Message = "Failed to initialize OpenVR: {Error}")]
    private static partial void LogOpenVRInitFailed(ILogger logger, LogLevel level, EVRInitError error);

    [LoggerMessage(Message = "Failed to register manifest: {Error}")]
    private static partial void LogManifestRegisterFailed(ILogger logger, LogLevel level, EVRApplicationError error);
}
