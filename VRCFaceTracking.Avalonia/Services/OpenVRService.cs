using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Valve.VR;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Services;

public class OpenVRService
{
    private static readonly TimeSpan EventPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(30);
    private static readonly uint EventSize = (uint)Marshal.SizeOf<VREvent_t>();

    private readonly ILogger<OpenVRService> _logger;
    private readonly object _initLock = new();
    private readonly CancellationTokenSource _serviceCts = new();
    private CVRSystem? _system;
    private CancellationTokenSource? _pollingCts;
    private Task? _reconnectLoop;
    private bool _nativeMissing;

    public static bool IsSupported => !OperatingSystem.IsMacOS();

    public event Action? QuitRequested;

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

            if (!IsSupported || _nativeMissing)
            {
                return false;
            }

            var error = EVRInitError.None;
            try
            {
                _system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException)
            {
                _nativeMissing = true;
                _logger.LogWarning("OpenVR native library not available: {Message}", ex.Message);
                return false;
            }

            if (error != EVRInitError.None)
            {
                _logger.Log(quiet ? LogLevel.Debug : LogLevel.Warning, "Failed to initialize OpenVR: {Error}", error);
                _system = null;
                return false;
            }

            var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;
            var fullManifestPath = Path.Combine(currentDirectory, "app.vrmanifest");
            var manifestRegisterResult = OpenVR.Applications.AddApplicationManifest(fullManifestPath, false);
            if (manifestRegisterResult != EVRApplicationError.None)
            {
                _logger.Log(quiet ? LogLevel.Debug : LogLevel.Warning, "Failed to register manifest: {Error}", manifestRegisterResult);
                _system = null;
                OpenVR.Shutdown();
                return false;
            }

            _logger.LogInformation("Successfully initialized OpenVR");
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
        _reconnectLoop ??= ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(ReconnectInterval);
            while (await timer.WaitForNextTickAsync(_serviceCts.Token))
            {
                if (!IsInitialized)
                {
                    Initialize(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(e, "OpenVR reconnect loop stopped");
        }
    }

    public void StopService()
    {
        _serviceCts.Cancel();
        Shutdown();
    }

    private async Task PumpEventsAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(EventPollInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                var system = _system;
                if (system == null || DrainEvents(system))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(e, "OpenVR event pump stopped unexpectedly");
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
                _logger.LogInformation("SteamVR is shutting down, closing VRCFaceTracking");
                system.AcknowledgeQuit_Exiting();
                Shutdown();
                QuitRequested?.Invoke();
            }
            else
            {
                _logger.LogInformation("SteamVR is shutting down, releasing OpenVR and staying open");
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
                _logger.LogWarning("Failed to set AutoStart preference. OpenVR couldn't be initialized.");
                return;
            }

            var setAutoLaunchResult = OpenVR.Applications.SetApplicationAutoLaunch("benaclejames.vrcft", value);
            if (setAutoLaunchResult != EVRApplicationError.None)
            {
                _logger.LogError("Failed to set auto launch: {Error}", setAutoLaunchResult);
            }
        }
    }
}
