using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Library;

public class UnifiedLibManager : ILibManager
{
    #region Logger
    private readonly ILogger<UnifiedLibManager> _logger;
    private readonly ILogger _moduleLogger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly LogLevelGate _logGate;
    #endregion

    #region Observables
    public ObservableCollection<ModuleMetadataInternal> LoadedModulesMetadata
    {
        get; set;
    }
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

    private static volatile bool _appShutdownRequested;
    private static volatile bool _teardownInProgress;
    public static bool IsTearingDown => _appShutdownRequested || _teardownInProgress;
    public static void MarkAppShutdown() => _appShutdownRequested = true;
    #endregion

    #region Modules

    private readonly List<ModuleRuntimeInfo> _moduleThreads = new();
    private readonly IModuleDataService _moduleDataService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly object _modulesLock = new();
    private readonly ModuleRestartPolicy _restartPolicy = new();
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(30);
    private int _initGeneration;
    private volatile bool _imageStreamEnabled;

    private const string CapabilityOverridesSettingKey = "ModuleCapabilityOverrides";
    private readonly object _overridesLock = new();
    private Dictionary<string, TrackingCapability> _capabilityOverrides = new();

    private string _sandboxProcessPath
    {
        get; set;
    }
    private readonly List<ModuleRuntimeInfo> AvailableSandboxModules = new();
    #endregion

    #region Thread
    private Thread _initializeWorker;
    private static VrcftSandboxServer _sandboxServer;
    #endregion

    public UnifiedLibManager(ILoggerFactory factory, IDispatcherService dispatcherService, IModuleDataService moduleDataService, ILocalSettingsService localSettingsService, LogLevelGate logGate)
    {
        _loggerFactory = factory;
        _logGate = logGate;
        _logGate.VerboseChanged += BroadcastVerbose;
        _logger = factory.CreateLogger<UnifiedLibManager>();
        _moduleLogger = factory.CreateLogger("\0VRCFT\0");
        _dispatcherService = dispatcherService;
        _moduleDataService = moduleDataService;
        _localSettingsService = localSettingsService;

        LoadedModulesMetadata = new ObservableCollection<ModuleMetadataInternal>();
        InitProgress = new ModuleInitProgress(dispatcherService);
        _sandboxProcessPath = Path.Combine(AppContext.BaseDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "VRCFaceTracking.ModuleProcess.exe" : "VRCFaceTracking.ModuleProcess");
        if (!File.Exists(_sandboxProcessPath))
        {
            // @TODO: Better error handling
            throw new FileNotFoundException($"Failed to find sandbox process at \"{_sandboxProcessPath}\"!");
        }

        // @TODO: Kill any lingering sub-modules to eliminate any conflicts
    }

    public void Initialize()
    {
        if (_initializeWorker != null && _initializeWorker.IsAlive)
        {
            _logger.LogWarning("Module initialization is already in progress; ignoring this request");
            return;
        }

        LoadedModulesMetadata.Clear();
        InitProgress.Begin();

        // Spawn sandbox server if it's null
        if (_sandboxServer == null)
        {
            // @TODO: Figure out an elegant way to ask the GUI for the ports the user assigned to the OSCTarget.
            var reservedPorts = new int[2] { 9000, 9001 };
            _sandboxServer = new VrcftSandboxServer(_loggerFactory, reservedPorts);
            _sandboxServer.OnPacketReceived += (in IpcPacket packet, in int port) =>
            {
                ModuleRuntimeInfo knownModule = null;
                lock (_modulesLock)
                {
                    for (var i = 0; i < AvailableSandboxModules.Count; i++)
                    {
                        if (AvailableSandboxModules[i].SandboxProcessPort == port)
                        {
                            knownModule = AvailableSandboxModules[i];
                            break;
                        }
                    }
                }

                switch (packet.GetPacketType())
                {
                    // @TODO: Move these all into methods to make the code easier to maintain
                    case IpcPacket.PacketType.Handshake:
                        {
                            // Look for the PID in the added modules list
                            var pkt = (HandshakePacket)packet;
                            if (!pkt.IsValid)
                            {
                                _logger.LogWarning("Ignoring invalid handshake from port {port}", port);
                                break;
                            }
                            lock (_modulesLock)
                            {
                                var pidRegistered = false;

                                for (var i = 0; i < AvailableSandboxModules.Count; i++)
                                {
                                    if (AvailableSandboxModules[i].SandboxProcessPID == pkt.PID)
                                    {
                                        var structCopy = AvailableSandboxModules[i];
                                        structCopy.SandboxProcessPort = port;
                                        AvailableSandboxModules[i] = structCopy;

                                        _logger.LogInformation("Initializing {module}...", AvailableSandboxModules[i].ModuleClassName.ToString());
                                        InitProgress.Advance(AvailableSandboxModules[i].SandboxModulePath, ModuleInitStage.Capabilities);
                                        AttemptSandboxedModuleInitialize(AvailableSandboxModules[i]);
                                        pidRegistered = true;

                                        break;
                                    }
                                }

                                if (pidRegistered == false)
                                {
                                    Process sandboxProcess;
                                    try
                                    {
                                        sandboxProcess = Process.GetProcessById(pkt.PID);
                                    }
                                    catch (ArgumentException)
                                    {
                                        _logger.LogWarning("Ignoring handshake from port {port}: no process with PID {pid}", port, pkt.PID);
                                        break;
                                    }

                                    var metadata = new ModuleMetadataInternal { ModulePath = pkt.ModulePath };
                                    lock (_overridesLock)
                                    {
                                        metadata.AllowedCapabilities = _capabilityOverrides.TryGetValue(pkt.ModulePath, out var stored) ? stored : null;
                                    }

                                    var nextSpawnOrder = AvailableSandboxModules.Count == 0
                                        ? 0
                                        : AvailableSandboxModules.Max(m => m.SpawnOrder) + 1;

                                    ModuleRuntimeInfo runtimeInfo = new ModuleRuntimeInfo()
                                    {
                                        SandboxProcessPID = pkt.PID,
                                        SandboxProcessPort = port,
                                        SandboxModulePath = pkt.ModulePath,
                                        IsActive = true,
                                        Process = sandboxProcess,
                                        ModuleClassName = Path.GetFileNameWithoutExtension(pkt.ModulePath),
                                        ModuleInformation = metadata,
                                        SpawnOrder = nextSpawnOrder,
                                        EventBus = new(),
                                    };
                                    AvailableSandboxModules.Add(runtimeInfo);

                                    _logger.LogInformation("Initializing {module}...", runtimeInfo.ModuleClassName);
                                    InitProgress.Advance(runtimeInfo.SandboxModulePath, ModuleInitStage.Capabilities);
                                    AttemptSandboxedModuleInitialize(runtimeInfo);
                                    pidRegistered = true;
                                }
                            }
                            break;
                        }

                    case IpcPacket.PacketType.EventLog:
                        {
                            EventLogPacket eventLogPacket = (EventLogPacket)packet;
                            _moduleLogger.Log(eventLogPacket.LogLevel, eventLogPacket.Message);
                            break;
                        }

                    case IpcPacket.PacketType.ReplyGetSupported:
                        {
                            if (knownModule == null)
                            {
                                _logger.LogWarning("Ignoring {type} packet from unregistered port {port}", packet.GetPacketType(), port);
                                break;
                            }

                            // We now know whether or not the module supports face or eye tracking
                            ReplySupportedPacket replySupportedPacket = (ReplySupportedPacket)packet;
                            var module = knownModule;

                            module.SupportsEyeTracking = replySupportedPacket.eyeAvailable;
                            module.SupportsExpressionTracking = replySupportedPacket.expressionAvailable;
                            module.ModuleInformation.SupportedEye = replySupportedPacket.eyeAvailable;
                            module.ModuleInformation.SupportedExpression = replySupportedPacket.expressionAvailable;

                            var allowedMask = module.ModuleInformation.AllowedCapabilities ?? TrackingCapability.All;

                            // Now tell it to initialise
                            EventInitPacket eventInitPacket = new EventInitPacket()
                            {
                                expressionAvailable = replySupportedPacket.expressionAvailable && (allowedMask & TrackingCapabilities.ExpressionHalf) != TrackingCapability.None,
                                eyeAvailable = replySupportedPacket.eyeAvailable && (allowedMask & TrackingCapabilities.EyeHalf) != TrackingCapability.None,
                            };
                            _logger.LogInformation("Got supported for module {module}. Expr: {} Eye: {}...",
                                module.ModuleClassName,
                                eventInitPacket.expressionAvailable,
                                eventInitPacket.eyeAvailable);
                            _sandboxServer.SendData(eventInitPacket, port);
                            InitProgress.Advance(module.SandboxModulePath, ModuleInitStage.Initializing);
                            break;
                        }

                    case IpcPacket.PacketType.ReplyInit:
                        {
                            if (knownModule == null)
                            {
                                _logger.LogWarning("Ignoring {type} packet from unregistered port {port}", packet.GetPacketType(), port);
                                break;
                            }

                            ReplyInitPacket replyInitPacket = (ReplyInitPacket)packet;
                            var module = knownModule;
                            module.ModuleInformation.Name = replyInitPacket.ModuleInformationName;

                            module.EyeInitialized = replyInitPacket.eyeSuccess;
                            module.ExpressionInitialized = replyInitPacket.expressionSuccess;

                            _logger.LogInformation("Got init for module {module}. Eye: {eye} Expr: {expr}...",
                                module.ModuleClassName,
                                replyInitPacket.eyeSuccess,
                                replyInitPacket.expressionSuccess);

                            // Skip any modules that don't succeed, otherwise set UnifiedLib to have these states active and add module to module list.
                            if (!replyInitPacket.eyeSuccess && !replyInitPacket.expressionSuccess)
                            {
                                InitProgress.Resolve(module.SandboxModulePath);
                                break;
                            }

                            var portCopy = port; // So that we can use it in the lambda method
                            void StatusChanged(object _, PropertyChangedEventArgs args)
                            {
                                if (args.PropertyName == nameof(ModuleMetadataInternal.AllowedCapabilities))
                                {
                                    OnAllowedCapabilitiesChanged(module);
                                    return;
                                }

                                if (args.PropertyName is not (nameof(ModuleMetadataInternal.Active)
                                    or nameof(ModuleMetadataInternal.UsingEye)
                                    or nameof(ModuleMetadataInternal.UsingExpression)))
                                {
                                    return;
                                }

                                module.Status = module.ModuleInformation.Active ? ModuleState.Active : ModuleState.Idle;

                                var statusUpdatePkt = new EventStatusUpdatePacket
                                {
                                    ModuleState = module.Status,
                                    UsingEye = module.ModuleInformation.UsingEye,
                                    UsingExpression = module.ModuleInformation.UsingExpression,
                                };
                                _sandboxServer.SendData(statusUpdatePkt, portCopy);

                                if (args.PropertyName == nameof(ModuleMetadataInternal.Active))
                                {
                                    RecomputeCapabilityAssignments();
                                }
                            }

                            if (module.StatusChangedHandler != null)
                            {
                                module.ModuleInformation.PropertyChanged -= module.StatusChangedHandler;
                            }
                            module.StatusChangedHandler = StatusChanged;
                            module.ModuleInformation.PropertyChanged += StatusChanged;

                            module.Status = ModuleState.Active;
                            module.ModuleInformation.Active = true;
                            module.ModuleInformation.StaticImages = replyInitPacket.IconDataStreams;
                            EnsureModuleThreadStartedSandboxed(module);
                            RecomputeCapabilityAssignments();
                            InitProgress.Resolve(module.SandboxModulePath);
                            if (_imageStreamEnabled)
                            {
                                _sandboxServer.SendData(new EventSetImageStreamPacket { Enabled = true }, port);
                            }

                            _dispatcherService.Run(() =>
                            {

                                // Check if the module is already loaded on the user-facing side. If so, overwrite with the new module if it's unloaded
                                var isModuleLoaded = false;
                                for (var i = 0; i < LoadedModulesMetadata.Count; i++)
                                {
                                    if (LoadedModulesMetadata[i].ModulePath != null &&
                                        LoadedModulesMetadata[i].ModulePath == module.ModuleInformation.ModulePath)
                                    {
                                        // Update module info
                                        LoadedModulesMetadata[i] = module.ModuleInformation;
                                        isModuleLoaded = true;
                                        break;
                                    }
                                }

                                // Add it to list if it was never loaded
                                if (isModuleLoaded == false)
                                {
                                    LoadedModulesMetadata.Add(module.ModuleInformation);
                                }

                                if (AvailableSandboxModules.Count == 0)
                                {
                                    _logger.LogWarning("No modules loaded.");
                                    LoadedModulesMetadata.Clear();
                                    LoadedModulesMetadata.Add(new ModuleMetadataInternal
                                    {
                                        Active = false,
                                        Name = "No Modules Loaded",
                                        IsPlaceholder = true
                                    });
                                }
                                else
                                {
                                    if (LoadedModulesMetadata.Count > 0 && LoadedModulesMetadata[0].IsPlaceholder)
                                    {
                                        LoadedModulesMetadata.RemoveAt(0);
                                    }

                                    if (module.ModuleInformation.Active)
                                    {
                                        _logger.LogInformation("Tracking initialized via {module}", module.ModuleClassName);
                                    }
                                }
                            });

                            break;
                        }
                    case IpcPacket.PacketType.ReplyUpdate:
                        {
                            if (knownModule == null)
                            {
                                break;
                            }

                            ReplyUpdatePacket replyUpdatePacket = (ReplyUpdatePacket)packet;
                            var module = knownModule;

                            if (module.Status == ModuleState.Active && module.ModuleInformation.Active)
                            {
                                lock (UnifiedTracking.DataLock)
                                {
                                    replyUpdatePacket.UpdateGlobalState(module.ModuleInformation.EffectiveCapabilities);
                                }
                            }

                            break;
                        }
                    case IpcPacket.PacketType.DebugStreamFrame:
                        {
                            if (knownModule == null || !_imageStreamEnabled)
                            {
                                break;
                            }

                            var framePacket = (ImageFrameUpdatePacket)packet;
                            var module = knownModule;
                            if (module.Status != ModuleState.Active || !module.ModuleInformation.Active)
                            {
                                break;
                            }

                            var frame = new Image
                            {
                                ImageSize = (framePacket.Width, framePacket.Height),
                                ImageData = framePacket.Data,
                                SupportsImage = true,
                            };
                            var capabilities = module.ModuleInformation.EffectiveCapabilities;
                            if (framePacket.Kind == ImageFrameUpdatePacket.EyeKind && capabilities.HasFlag(TrackingCapability.Eyes))
                            {
                                UnifiedTracking.EyeImageData = frame;
                            }
                            else if (framePacket.Kind == ImageFrameUpdatePacket.LipKind && capabilities.HasFlag(TrackingCapability.Mouth))
                            {
                                UnifiedTracking.LipImageData = frame;
                            }

                            break;
                        }
                }
            };
        }

        Interlocked.Increment(ref _initGeneration);
        _restartPolicy.ResetAll();

        // Start Initialization
        _initializeWorker = new Thread(() =>
        {
            try
            {
                // Kill lingering threads
                TeardownAllAndReset();

                LoadCapabilityOverrides();

                // Find all modules
                var modules = _moduleDataService.GetInstalledModules().Concat(_moduleDataService.GetLegacyModules());
                var modulePaths = modules.Select(m => m.AssemblyLoadPath);

                var startedAny = false;
                lock (_modulesLock)
                {
                    AvailableSandboxModules.Clear();
                }
                InitialiseSandboxesBaseOnPaths(modulePaths.ToArray());
                lock (_modulesLock)
                {
                    startedAny = AvailableSandboxModules.Count > 0;
                }

                if (startedAny)
                {
                    _logger.LogDebug("Initializing requested runtimes...");
                    var generation = Volatile.Read(ref _initGeneration);
                    _ = Task.Delay(InitTimeout).ContinueWith(async _ =>
                    {
                        if (generation != Volatile.Read(ref _initGeneration))
                        {
                            return;
                        }

                        InitProgress.TimeOutPending();
                        await Task.Delay(TimeSpan.FromSeconds(15));
                        if (generation == Volatile.Read(ref _initGeneration))
                        {
                            InitProgress.Finish();
                        }
                    });
                }
                else
                {
                    InitProgress.Finish();
                    _dispatcherService.Run(() =>
                    {
                        LoadedModulesMetadata.Clear();
                        LoadedModulesMetadata.Add(new ModuleMetadataInternal
                        {
                            Active = false,
                            Name = "No Modules Loaded",
                            IsPlaceholder = true
                        });
                    });
                    _logger.LogWarning("No modules loaded.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module initialization failed");
            }
        });
        _initializeWorker.IsBackground = true;
        _logger.LogInformation("Starting initialization tracking");
        _initializeWorker.Start();
    }

    private void LoadCapabilityOverrides()
    {
        try
        {
            var stored = _localSettingsService
                .ReadSettingAsync<Dictionary<string, TrackingCapability>>(CapabilityOverridesSettingKey, forceLocal: true)
                .GetAwaiter().GetResult();
            lock (_overridesLock)
            {
                _capabilityOverrides = stored ?? new Dictionary<string, TrackingCapability>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load module capability overrides: {message}", ex.Message);
        }
    }

    private void OnAllowedCapabilitiesChanged(ModuleRuntimeInfo module)
    {
        var info = module.ModuleInformation;
        var path = module.SandboxModulePath;
        Dictionary<string, TrackingCapability> snapshot;
        lock (_overridesLock)
        {
            if (info.AllowedCapabilities is { } allowed)
            {
                _capabilityOverrides[path] = allowed;
            }
            else
            {
                _capabilityOverrides.Remove(path);
            }
            snapshot = new Dictionary<string, TrackingCapability>(_capabilityOverrides);
        }

        _ = SaveCapabilityOverridesAsync(snapshot);

        var allowedMask = info.AllowedCapabilities ?? TrackingCapability.All;
        if (((allowedMask & TrackingCapabilities.EyeHalf) != TrackingCapability.None && info.SupportedEye && !module.EyeInitialized) ||
            ((allowedMask & TrackingCapabilities.ExpressionHalf) != TrackingCapability.None && info.SupportedExpression && !module.ExpressionInitialized))
        {
            _logger.LogInformation("{module} was initialized without one of the halves now allowed; restart modules to activate it", module.ModuleClassName);
        }

        RecomputeCapabilityAssignments();
    }

    private async Task SaveCapabilityOverridesAsync(Dictionary<string, TrackingCapability> snapshot)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync(CapabilityOverridesSettingKey, snapshot, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to save module capability overrides: {message}", ex.Message);
        }
    }

    private void RecomputeCapabilityAssignments() => _dispatcherService.Run(() =>
    {
        ModuleRuntimeInfo[] modules;
        lock (_modulesLock)
        {
            modules = AvailableSandboxModules.OrderBy(m => m.SpawnOrder).ToArray();
        }

        var participants = new List<ModuleRuntimeInfo>(modules.Length);
        var candidates = new List<CapabilityCandidate>(modules.Length);
        foreach (var module in modules)
        {
            var info = module.ModuleInformation;
            var participates = !module.TeardownRequested && info.Active && !info.Crashed &&
                               (module.EyeInitialized || module.ExpressionInitialized);
            if (participates)
            {
                participants.Add(module);
                candidates.Add(new CapabilityCandidate(module.EyeInitialized, module.ExpressionInitialized, info.AllowedCapabilities));
            }
            else
            {
                ApplyAssignment(info, TrackingCapability.None);
            }
        }

        var assigned = CapabilityAssigner.Assign(candidates);
        for (var i = 0; i < participants.Count; i++)
        {
            ApplyAssignment(participants[i].ModuleInformation, assigned[i]);
        }

        EyeStatus = participants.Any(m => m.EyeInitialized) ? ModuleState.Active : ModuleState.Uninitialized;
        ExpressionStatus = participants.Any(m => m.ExpressionInitialized) ? ModuleState.Active : ModuleState.Uninitialized;
    });

    private static void ApplyAssignment(ModuleMetadataInternal info, TrackingCapability assigned)
    {
        info.EffectiveCapabilities = assigned;
        info.UsingEye = (assigned & TrackingCapabilities.EyeHalf) != TrackingCapability.None;
        info.UsingExpression = (assigned & TrackingCapabilities.ExpressionHalf) != TrackingCapability.None;
    }

    private void InitialiseSandboxesBaseOnPaths(IEnumerable<string> paths)
    {
        var dlls = paths.ToArray();
        foreach (var dll in dlls)
        {
            InitProgress.Add(dll, Path.GetFileNameWithoutExtension(dll));
        }

        for (var i = 0; i < dlls.Length; i++)
        {
            if (!StartModuleProcess(dlls[i], new ModuleMetadataInternal(), i))
            {
                InitProgress.Resolve(dlls[i]);
            }
        }
    }

    private bool StartModuleProcess(string dll, ModuleMetadataInternal metadata, int spawnOrder)
    {
        try
        {
            metadata.ModulePath = dll;
            lock (_overridesLock)
            {
                metadata.AllowedCapabilities = _capabilityOverrides.TryGetValue(dll, out var stored) ? stored : null;
            }
            var verboseFlag = _logGate.Verbose ? " --verbose" : string.Empty;
            var sandboxProcess = Process.Start(new ProcessStartInfo(
                _sandboxProcessPath, $"--port {_sandboxServer.Port} --module-path \"{dll}\" --parent-pid {Environment.ProcessId}{verboseFlag}"
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            })!;

            var pid = sandboxProcess.Id;

            // Add the module info into the loaded list
            ModuleRuntimeInfo runtimeInfo = new ModuleRuntimeInfo()
            {
                SandboxProcessPID = pid,
                SandboxProcessPort = -1,
                SandboxModulePath = dll,
                IsActive = true,
                Process = sandboxProcess,
                ModuleClassName = Path.GetFileNameWithoutExtension(dll),
                ModuleInformation = metadata,
                SpawnOrder = spawnOrder,
                EventBus = new()
            };
            lock (_modulesLock)
            {
                _logger.LogInformation("Started module process {pid} for {dllPath}", pid, dll);
                AvailableSandboxModules.Add(runtimeInfo);
            }
            runtimeInfo.Watcher = new ModuleProcessWatcher(sandboxProcess, runtimeInfo.ModuleClassName, _moduleLogger, code => OnModuleProcessExited(runtimeInfo, code));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning("{error} Failed to start sandbox process for {path}. Skipping...", e.Message, dll);
            return false;
        }
    }

    private void OnModuleProcessExited(ModuleRuntimeInfo module, int exitCode)
    {
        module.UpdateCancellationToken?.Cancel();
        module.IsActive = false;

        if (exitCode == ModuleProcessExitCodes.OK || module.TeardownRequested || IsTearingDown)
        {
            _logger.LogInformation("Module process for {module} exited ({description})", module.ModuleClassName, ModuleProcessExitCodes.Describe(exitCode));
            return;
        }

        var description = ModuleProcessExitCodes.Describe(exitCode);
        var stderrTail = module.Watcher?.StderrTail ?? string.Empty;
        _logger.LogError("Module process for {module} stopped unexpectedly: {description}. Last stderr lines:{newline}{tail}",
            module.ModuleClassName, description, Environment.NewLine, stderrTail);

        if (ModuleRestartPolicy.IsRestartableExitCode(exitCode) && !string.IsNullOrEmpty(module.SandboxModulePath))
        {
            var delay = _restartPolicy.NextRestartDelay(module.SandboxModulePath, DateTime.UtcNow);
            if (delay.HasValue)
            {
                var attempt = _restartPolicy.AttemptNumber(module.SandboxModulePath);
                _logger.LogWarning("Restarting {module} after {description} (attempt {attempt}/{max})",
                    module.ModuleClassName, description, attempt, ModuleRestartPolicy.MaxAttempts);
                _ = RestartModuleAsync(module, delay.Value, Volatile.Read(ref _initGeneration));
                return;
            }

            _logger.LogError("{module} crashed {max} times within {window}s; leaving it stopped",
                module.ModuleClassName, ModuleRestartPolicy.MaxAttempts, (int)ModuleRestartPolicy.Window.TotalSeconds);
        }

        MarkModuleCrashed(module, description);
    }

    private void MarkModuleCrashed(ModuleRuntimeInfo module, string description)
    {
        InitProgress.Resolve(module.SandboxModulePath);
        _dispatcherService.Run(() =>
        {
            var info = module.ModuleInformation;
            if (string.IsNullOrEmpty(info.Name))
            {
                info.Name = module.ModuleClassName;
            }

            if (LoadedModulesMetadata.Count > 0 && LoadedModulesMetadata[0].IsPlaceholder)
            {
                LoadedModulesMetadata.RemoveAt(0);
            }

            if (!LoadedModulesMetadata.Contains(info))
            {
                LoadedModulesMetadata.Add(info);
            }

            info.Active = false;
            module.Status = ModuleState.Crashed;
            info.CrashDescription = description;
            info.Crashed = true;
            RecomputeCapabilityAssignments();
        });
    }

    private async Task RestartModuleAsync(ModuleRuntimeInfo deadModule, TimeSpan delay, int generation)
    {
        try
        {
            lock (_modulesLock)
            {
                _moduleThreads.Remove(deadModule);
                AvailableSandboxModules.Remove(deadModule);
            }

            var info = deadModule.ModuleInformation;
            ApplyAssignment(info, TrackingCapability.None);
            RecomputeCapabilityAssignments();

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            if (IsTearingDown || deadModule.TeardownRequested || generation != Volatile.Read(ref _initGeneration))
            {
                return;
            }

            info.Crashed = false;
            info.CrashDescription = string.Empty;

            deadModule.Process?.Dispose();

            if (deadModule.StatusChangedHandler != null)
            {
                info.PropertyChanged -= deadModule.StatusChangedHandler;
                deadModule.StatusChangedHandler = null;
            }

            if (StartModuleProcess(deadModule.SandboxModulePath, info, deadModule.SpawnOrder))
            {
                _logger.LogInformation("Restarted module process for {module}", deadModule.ModuleClassName);
            }
            else
            {
                MarkModuleCrashed(deadModule, "failed to restart the module process");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart {module}", deadModule.ModuleClassName);
        }
    }

    public void SetImageStreamEnabled(bool enabled)
    {
        _imageStreamEnabled = enabled;
        var packet = new EventSetImageStreamPacket { Enabled = enabled };
        ModuleRuntimeInfo[] modules;
        lock (_modulesLock)
        {
            modules = AvailableSandboxModules.ToArray();
        }

        foreach (var module in modules)
        {
            if (module.SandboxProcessPort > 0 && !(module.Process?.HasExited ?? true))
            {
                _sandboxServer?.SendData(packet, module.SandboxProcessPort);
            }
        }

        if (!enabled)
        {
            UnifiedTracking.EyeImageData = new Image();
            UnifiedTracking.LipImageData = new Image();
        }
    }

    private void BroadcastVerbose(bool verbose)
    {
        var packet = new EventSetVerbosePacket { Verbose = verbose };
        ModuleRuntimeInfo[] modules;
        lock (_modulesLock)
        {
            modules = AvailableSandboxModules.ToArray();
        }

        foreach (var module in modules)
        {
            if (module.SandboxProcessPort > 0 && !(module.Process?.HasExited ?? true))
            {
                _sandboxServer.SendData(packet, module.SandboxProcessPort);
            }
        }
    }

    private void EnsureModuleThreadStartedSandboxed(ModuleRuntimeInfo module)
    {
        lock (_modulesLock)
        {
            if (_moduleThreads.Any(pair =>
                (pair.SandboxProcessPID == module.SandboxProcessPID) &&
                (pair.SandboxProcessPort == module.SandboxProcessPort)
            ))
            {
                return;
            }
        }

        var port = module.SandboxProcessPort;

        var cts = new CancellationTokenSource();
        var thread = new Thread(() =>
        {
            _logger.LogDebug("Starting thread for {module}", module.GetType().Name);
            var updatePacket = new EventUpdatePacket();
            while (!cts.IsCancellationRequested)
            {
                Thread.Sleep(10); // Wait 10ms => 100Hz
                _sandboxServer.SendData(updatePacket, port);
            }
            _logger.LogDebug("Thread for {module} ended", module.GetType().Name);
        });
        thread.IsBackground = true;
        thread.Start();
        module.UpdateCancellationToken = cts;
        module.UpdateThread = thread;

        lock (_modulesLock)
        {
            _moduleThreads.Add(module);
        }
    }

    private void AttemptSandboxedModuleInitialize(ModuleRuntimeInfo module)
    {
        // Tell the sandbox to call the initialize function on the module
        var eventGetSupportedPacket = new EventInitGetSupported();

        // If PID is valid and we know which port the sandbox process is running on
        if (module.SandboxProcessPID != -1 && module.SandboxProcessPort > 0)
        {
            _sandboxServer.SendData(eventGetSupportedPacket, module.SandboxProcessPort);
        }
        else
        {
            // Queue the packet so that we send it after we know which process the sandbox process is running on
            QueuedPacket queuedPacket = new QueuedPacket()
            {
                packet = eventGetSupportedPacket,
                destinationPort = module.SandboxProcessPort
            };
            module.EventBus.Enqueue(queuedPacket);
        }
    }

    private bool TeardownModuleSandboxed(ModuleRuntimeInfo module)
    {
        _logger.LogInformation("Tearing down {module} ", module.ModuleClassName);
        module.TeardownRequested = true;

        // Send a message to the module sub-process
        if (module.SandboxProcessPort > 0)
        {
            var eventTeardownPacket = new EventTeardownPacket();
            _sandboxServer.SendData(eventTeardownPacket, module.SandboxProcessPort);
        }

        // Kill the update thread
        module.UpdateCancellationToken?.Cancel();
        // Give the module 100ms to kill itself
        Thread.Sleep(100);

        // Only bother tearing down a module if it's actually shutdown
        if (!(module.Process?.HasExited ?? true))
        {
            _logger.LogDebug("Module process has not yet exited");
            try
            {
                if (!(module.Process?.WaitForExit(1000) ?? false))
                {
                    _logger.LogDebug("Module {id} didn't exit gracefully. Forcing kill...", module.Process?.Id ?? -1);
                    module.Process?.Kill(entireProcessTree: true);
                    if (!(module.Process?.WaitForExit(2000) ?? false))
                    {
                        // on windows we can use taskkill /F /T /PID {procId} to force kill a process very aggressively. this has a higher success rate than process.kill!
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            using var killer = Process.Start(new ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/F /T /PID {module.Process.Id}",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                            killer?.WaitForExit(2000);
                        }
                        else
                        {
                            _logger.LogCritical("Process {id} is a zombie or stuck in Kernel I/O. Manual intervention required.", module.Process.Id);
                        }
                        return false;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Can fail to call OpenProcessEx due to some error such as ACCESS_DENIED (process has higher priveleges, eg Sraniple)
                _logger.LogError($"Tried killing process with PID {module.Process.Id}. Got win32 error ({ex.ToString()}");
            }
            catch (Exception ex)
            {
                // Tell the user why we got an exception so that we can hopefully fix it.
                _logger.LogError($"Tried killing process with PID {module.Process.Id}. Got exception ({ex.HResult}) {ex.Message}");
            }
        }

        if (module.UpdateThread?.IsAlive ?? false)
        {
            // Edge case, we wait for the thread to finish before unloading the assembly
            var moduleName = module.ModuleInformation?.Name ?? module.ModuleClassName ?? "Unknown";
            _logger.LogDebug("Waiting for {module}'s thread to join...", moduleName);
            module.UpdateThread?.Join(500);
        }

        return true;
    }

    // Signal all active modules to gracefully shut down their respective runtimes
    public void TeardownAllAndReset()
    {
        _logger.LogInformation("Tearing down all modules...");
        _teardownInProgress = true;
        try
        {
            ModuleRuntimeInfo[] threadModules;
            ModuleRuntimeInfo[] sandboxModules;
            lock (_modulesLock)
            {
                threadModules = _moduleThreads.ToArray();
                sandboxModules = AvailableSandboxModules.ToArray();
            }

            foreach (var module in threadModules.Concat(sandboxModules))
            {
                if (module != null)
                {
                    module.TeardownRequested = true;
                }
            }

            foreach (var module in threadModules)
            {
                TeardownOneModule(module);
            }
            lock (_modulesLock)
            {
                _moduleThreads.Clear();
            }

            foreach (var module in sandboxModules)
            {
                TeardownOneModule(module);
            }
            lock (_modulesLock)
            {
                AvailableSandboxModules.Clear();
            }

            foreach (var module in threadModules.Concat(sandboxModules).Distinct())
            {
                module?.Process?.Dispose();
            }

            EyeStatus = ModuleState.Uninitialized;
            ExpressionStatus = ModuleState.Uninitialized;
        }
        finally
        {
            _teardownInProgress = false;
        }
    }

    public static void ShutdownSandboxServer()
    {
        var server = _sandboxServer;
        _sandboxServer = null;
        server?.Close();
    }

    private void TeardownOneModule(ModuleRuntimeInfo module)
    {
        if (module == null || (module.Process?.HasExited ?? true))
        {
            return;
        }

        var success = false;
        try
        {
            success = TeardownModuleSandboxed(module);
        }
        finally
        {
            if (!success)
            {
                var moduleName = module.ModuleInformation?.Name ?? module.ModuleClassName ?? "Unknown";
                _logger.LogWarning("Module: {module} failed to shut down. Killing its thread.", moduleName);
                module.UpdateThread?.Interrupt();
            }
        }
    }
}