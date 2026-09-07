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
    private CVRSystem? _system;
    private CancellationTokenSource? _pollingCts;
    private Task? _reconnectLoop;

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

            var error = EVRInitError.None;
            _system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);

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
        _reconnectLoop ??= ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        using var timer = new PeriodicTimer(ReconnectInterval);
        while (await timer.WaitForNextTickAsync())
        {
            if (!IsInitialized)
            {
                Initialize(true);
            }
        }
    }

    private async Task PumpEventsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(EventPollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_system == null || DrainEvents())
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool DrainEvents()
    {
        var vrEvent = new VREvent_t();
        while (_system!.PollNextEvent(ref vrEvent, EventSize))
        {
            if ((EVREventType)vrEvent.eventType != EVREventType.VREvent_Quit)
            {
                continue;
            }

            if (ExitWithSteamVr)
            {
                _logger.LogInformation("SteamVR is shutting down, closing VRCFaceTracking");
                _system.AcknowledgeQuit_Exiting();
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
