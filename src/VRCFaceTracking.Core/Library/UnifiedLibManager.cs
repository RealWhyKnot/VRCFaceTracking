using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Library;

public sealed partial class UnifiedLibManager : ILibManager
{
    #region Logger
    private readonly ILogger<UnifiedLibManager> _logger;
    private readonly ILogger _moduleLogger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly LogLevelGate _logGate;
    private readonly IpcLatencyDiagnostics _ipcLatency;
    #endregion

    #region Observables
    public ObservableCollection<ModuleMetadataInternal> LoadedModulesMetadata
    {
        get;
    } = [];
    public ModuleInitProgress InitProgress
    {
        get;
    }
    private readonly IDispatcherService _dispatcherService;
    #endregion

    #region Statuses
    public static ModuleState EyeStatus
    {
        get; private set;
    }
    public static ModuleState ExpressionStatus
    {
        get; private set;
    }

    private static volatile bool AppShutdownRequested;
    private static volatile bool TeardownInProgress;
    public static bool IsTearingDown => AppShutdownRequested || TeardownInProgress;
    public static void MarkAppShutdown() => AppShutdownRequested = true;
    #endregion

    #region Modules

    private readonly List<ModuleRuntimeInfo> _moduleThreads = [];
    private readonly IModuleDataService _moduleDataService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly Lock _modulesLock = new();
    private readonly ModuleRestartPolicy _restartPolicy = new();
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(30);
    private int _initGeneration;
    private volatile bool _imageStreamEnabled;

    private const string CapabilityOverridesSettingKey = "ModuleCapabilityOverrides";
    private const int MaxModuleLogLength = 16384;
    private readonly Lock _overridesLock = new();
    private Dictionary<string, TrackingCapability> _capabilityOverrides = [];

    private string SandboxProcessPath
    {
        get; set;
    }
    private readonly List<ModuleRuntimeInfo> _availableSandboxModules = [];
    #endregion

    #region Thread
    private Task? _initializeWorker;
    private static VrcftSandboxServer? SandboxServer;
    #endregion

    public UnifiedLibManager(ILoggerFactory factory, IDispatcherService dispatcherService, IModuleDataService moduleDataService, ILocalSettingsService localSettingsService, LogLevelGate logGate, TimeProvider timeProvider)
    {
        _loggerFactory = factory;
        _timeProvider = timeProvider;
        _logGate = logGate;
        _logGate.VerboseChanged += BroadcastVerbose;
        _logger = factory.CreateLogger<UnifiedLibManager>();
        _moduleLogger = factory.CreateLogger("\0VRCFT\0");
        _ipcLatency = new IpcLatencyDiagnostics(_logger);
        _dispatcherService = dispatcherService;
        _moduleDataService = moduleDataService;
        _localSettingsService = localSettingsService;

        InitProgress = new ModuleInitProgress(dispatcherService);
        SandboxProcessPath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "VRCFaceTracking.ModuleProcess.exe" : "VRCFaceTracking.ModuleProcess");
        if (!File.Exists(SandboxProcessPath))
        {
            throw new FileNotFoundException($"Failed to find sandbox process at \"{SandboxProcessPath}\"!");
        }
    }

    public void Initialize()
    {
        if (_initializeWorker is { IsCompleted: false })
        {
            LogModuleInitializationAlreadyProgressIgnoring(_logger);
            return;
        }

        LoadedModulesMetadata.Clear();
        InitProgress.Begin();

        if (SandboxServer == null)
        {
            int[] reservedPorts = [9000, 9001];
            var server = new VrcftSandboxServer(_loggerFactory, reservedPorts);
            SandboxServer = server;
            server.OnPacketReceived += (in packet, in port) => HandlePacket(server, packet, port);
        }

        Interlocked.Increment(ref _initGeneration);
        _restartPolicy.ResetAll();

        LogStartingInitializationTracking(_logger);
        _initializeWorker = Task.Run(() => InitializeModulesAsync(CancellationToken.None));
    }

    private async Task InitializeModulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            TeardownAllAndReset();

            await LoadCapabilityOverridesAsync(cancellationToken).ConfigureAwait(false);

            IEnumerable<InstallableTrackingModule> modules = _moduleDataService.GetInstalledModules().Concat(_moduleDataService.GetLegacyModules());
            string[] modulePaths = [.. modules.Select(m => m.AssemblyLoadPath)];

            lock (_modulesLock)
            {
                _availableSandboxModules.Clear();
            }
            StartModuleProcesses(modulePaths);
            bool startedAny;
            lock (_modulesLock)
            {
                startedAny = _availableSandboxModules.Count > 0;
            }

            if (startedAny)
            {
                LogInitializingRequestedRuntimes(_logger);
                _ = FinishInitAfterTimeoutAsync(Volatile.Read(ref _initGeneration), cancellationToken);
            }
            else
            {
                InitProgress.Finish();
                _dispatcherService.Run(ShowNoModulesPlaceholder);
                LogNoModulesLoaded(_logger);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogModuleInitializationFailed(_logger, ex);
        }
    }

    private async Task FinishInitAfterTimeoutAsync(int generation, CancellationToken cancellationToken)
    {
        await Task.Delay(InitTimeout, cancellationToken).ConfigureAwait(false);
        if (generation != Volatile.Read(ref _initGeneration))
        {
            return;
        }

        InitProgress.TimeOutPending();
        await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (generation == Volatile.Read(ref _initGeneration))
        {
            InitProgress.Finish();
        }
    }

    private void ShowNoModulesPlaceholder()
    {
        LoadedModulesMetadata.Clear();
        LoadedModulesMetadata.Add(new ModuleMetadataInternal
        {
            Active = false,
            Name = "No Modules Loaded",
            IsPlaceholder = true,
        });
    }

    public void SetImageStreamEnabled(bool enabled)
    {
        _imageStreamEnabled = enabled;
        SendToLiveModules(new EventSetImageStreamPacket { Enabled = enabled });

        if (!enabled)
        {
            UnifiedTracking.EyeImageData = new Image();
            UnifiedTracking.LipImageData = new Image();
        }
    }

    private void BroadcastVerbose(object? sender, EventArgs e) => SendToLiveModules(new EventSetVerbosePacket { Verbose = _logGate.Verbose });

    private void SendToLiveModules(IpcPacket packet)
    {
        ModuleRuntimeInfo[] modules;
        lock (_modulesLock)
        {
            modules = [.. _availableSandboxModules];
        }

        foreach (ModuleRuntimeInfo module in modules)
        {
            if (module.SandboxProcessPort > 0 && !(module.Process?.HasExited ?? true))
            {
                SandboxServer?.SendData(packet, module.SandboxProcessPort);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Module initialization is already in progress; ignoring this request")]
    private static partial void LogModuleInitializationAlreadyProgressIgnoring(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No modules loaded.")]
    private static partial void LogNoModulesLoaded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initializing requested runtimes...")]
    private static partial void LogInitializingRequestedRuntimes(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module initialization failed")]
    private static partial void LogModuleInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting initialization tracking")]
    private static partial void LogStartingInitializationTracking(ILogger logger);
}
