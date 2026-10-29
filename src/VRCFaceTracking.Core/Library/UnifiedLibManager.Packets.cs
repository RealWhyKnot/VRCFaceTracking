using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Library;

public sealed partial class UnifiedLibManager
{
    private void HandlePacket(VrcftSandboxServer server, IpcPacket packet, int port)
    {
        IpcPacket.PacketType type = packet.GetPacketType();
        switch (type)
        {
            case IpcPacket.PacketType.Handshake:
                HandleHandshake((HandshakePacket)packet, port);
                break;
            case IpcPacket.PacketType.EventLog:
                HandleEventLog((EventLogPacket)packet);
                break;
            case IpcPacket.PacketType.ReplyGetSupported:
                if (FindModuleByPort(port) is { } supportedModule)
                {
                    HandleReplyGetSupported(server, (ReplySupportedPacket)packet, supportedModule, port);
                }
                else
                {
                    LogIgnoringPacketUnregisteredPort(_logger, type, port);
                }
                break;
            case IpcPacket.PacketType.ReplyInit:
                if (FindModuleByPort(port) is { } initModule)
                {
                    HandleReplyInit(server, (ReplyInitPacket)packet, initModule, port);
                }
                else
                {
                    LogIgnoringPacketUnregisteredPort(_logger, type, port);
                }
                break;
            case IpcPacket.PacketType.ReplyUpdate:
                if (FindModuleByPort(port) is { } updateModule)
                {
                    HandleReplyUpdate((ReplyUpdatePacket)packet, updateModule);
                }
                break;
            case IpcPacket.PacketType.DebugStreamFrame:
                if (_imageStreamEnabled && FindModuleByPort(port) is { } frameModule)
                {
                    HandleDebugStreamFrame((ImageFrameUpdatePacket)packet, frameModule);
                }
                break;
            case IpcPacket.PacketType.Unknown:
            case IpcPacket.PacketType.SplitPacketChunk:
            case IpcPacket.PacketType.EventGetSupported:
            case IpcPacket.PacketType.EventInit:
            case IpcPacket.PacketType.EventTeardown:
            case IpcPacket.PacketType.EventUpdate:
            case IpcPacket.PacketType.EventUpdateStatus:
            case IpcPacket.PacketType.EventSetVerbose:
            case IpcPacket.PacketType.EventSetImageStream:
            case IpcPacket.PacketType.ReplyTeardown:
            default:
                break;
        }
    }

    private ModuleRuntimeInfo? FindModuleByPort(int port)
    {
        lock (_modulesLock)
        {
            for (int i = 0; i < _availableSandboxModules.Count; i++)
            {
                if (_availableSandboxModules[i].SandboxProcessPort == port)
                {
                    return _availableSandboxModules[i];
                }
            }
        }

        return null;
    }

    private void HandleHandshake(HandshakePacket handshake, int port)
    {
        if (!handshake.IsValid)
        {
            LogIgnoringInvalidHandshakePort(_logger, port);
            return;
        }

        lock (_modulesLock)
        {
            ModuleRuntimeInfo? module = null;
            for (int i = 0; i < _availableSandboxModules.Count; i++)
            {
                if (_availableSandboxModules[i].SandboxProcessPID == handshake.ProcessId)
                {
                    module = _availableSandboxModules[i];
                    module.SandboxProcessPort = port;
                    break;
                }
            }

            module ??= AdoptModuleProcess(handshake, port);
            if (module == null)
            {
                return;
            }

            LogInitializing(_logger, module.ModuleClassName);
            InitProgress.Advance(module.SandboxModulePath, ModuleInitStage.Capabilities);
            AttemptSandboxedModuleInitialize(module);
        }
    }

    private ModuleRuntimeInfo? AdoptModuleProcess(HandshakePacket handshake, int port)
    {
        Process sandboxProcess;
        try
        {
            sandboxProcess = Process.GetProcessById(handshake.ProcessId);
        }
        catch (ArgumentException)
        {
            LogIgnoringHandshakePortNoProcess(_logger, port, handshake.ProcessId);
            return null;
        }

        var metadata = new ModuleMetadataInternal
        {
            ModulePath = handshake.ModulePath,
            AllowedCapabilities = StoredCapabilityOverride(handshake.ModulePath),
        };

        int nextSpawnOrder = _availableSandboxModules.Count == 0
            ? 0
            : _availableSandboxModules.Max(m => m.SpawnOrder) + 1;

        var runtimeInfo = new ModuleRuntimeInfo()
        {
            SandboxProcessPID = handshake.ProcessId,
            SandboxProcessPort = port,
            SandboxModulePath = handshake.ModulePath,
            IsActive = true,
            Process = sandboxProcess,
            ModuleClassName = Path.GetFileNameWithoutExtension(handshake.ModulePath),
            ModuleInformation = metadata,
            SpawnOrder = nextSpawnOrder,
        };
        _availableSandboxModules.Add(runtimeInfo);
        return runtimeInfo;
    }

    private void HandleEventLog(EventLogPacket eventLog) =>
        LogModuleMessage(_moduleLogger, eventLog.LogLevel, eventLog.Message.Length > MaxModuleLogLength ? eventLog.Message[..MaxModuleLogLength] + "…" : eventLog.Message);

    private void HandleReplyGetSupported(VrcftSandboxServer server, ReplySupportedPacket reply, ModuleRuntimeInfo module, int port)
    {
        module.SupportsEyeTracking = reply.EyeAvailable;
        module.SupportsExpressionTracking = reply.ExpressionAvailable;
        module.ModuleInformation.SupportedEye = reply.EyeAvailable;
        module.ModuleInformation.SupportedExpression = reply.ExpressionAvailable;

        TrackingCapability allowedMask = module.ModuleInformation.AllowedCapabilities ?? TrackingCapability.All;

        var eventInitPacket = new EventInitPacket()
        {
            ExpressionAvailable = reply.ExpressionAvailable && (allowedMask & TrackingCapabilities.ExpressionHalf) != TrackingCapability.None,
            EyeAvailable = reply.EyeAvailable && (allowedMask & TrackingCapabilities.EyeHalf) != TrackingCapability.None,
        };
        LogGotSupportedModuleExprEye(_logger, module.ModuleClassName, eventInitPacket.ExpressionAvailable, eventInitPacket.EyeAvailable);
        server.SendData(eventInitPacket, port);
        InitProgress.Advance(module.SandboxModulePath, ModuleInitStage.Initializing);
    }

    private void HandleReplyInit(VrcftSandboxServer server, ReplyInitPacket reply, ModuleRuntimeInfo module, int port)
    {
        module.ModuleInformation.Name = reply.ModuleInformationName;
        module.EyeInitialized = reply.EyeSuccess;
        module.ExpressionInitialized = reply.ExpressionSuccess;

        LogGotInitModuleEyeExpr(_logger, module.ModuleClassName, reply.EyeSuccess, reply.ExpressionSuccess);

        if (!reply.EyeSuccess && !reply.ExpressionSuccess)
        {
            InitProgress.Resolve(module.SandboxModulePath);
            return;
        }

        WatchModuleStatus(server, module, port);

        module.Status = ModuleState.Active;
        module.ModuleInformation.Active = true;
        module.ModuleInformation.StaticImages = reply.IconDataStreams;
        EnsureModuleThreadStartedSandboxed(module);
        RecomputeCapabilityAssignments();
        InitProgress.Resolve(module.SandboxModulePath);
        if (_imageStreamEnabled)
        {
            server.SendData(new EventSetImageStreamPacket { Enabled = true }, port);
        }

        _dispatcherService.Run(() => PublishLoadedModule(module));
    }

    private void WatchModuleStatus(VrcftSandboxServer server, ModuleRuntimeInfo module, int port)
    {
        if (module.StatusChangedHandler != null)
        {
            module.ModuleInformation.PropertyChanged -= module.StatusChangedHandler;
        }

        void StatusChanged(object? _, PropertyChangedEventArgs args) => OnModuleStatusChanged(server, module, port, args.PropertyName);

        module.StatusChangedHandler = StatusChanged;
        module.ModuleInformation.PropertyChanged += StatusChanged;
    }

    private void OnModuleStatusChanged(VrcftSandboxServer server, ModuleRuntimeInfo module, int port, string? propertyName)
    {
        if (string.Equals(propertyName, nameof(ModuleMetadataInternal.AllowedCapabilities), StringComparison.Ordinal))
        {
            OnAllowedCapabilitiesChanged(module);
            return;
        }

        if (propertyName is not (nameof(ModuleMetadataInternal.Active)
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
        server.SendData(statusUpdatePkt, port);

        if (string.Equals(propertyName, nameof(ModuleMetadataInternal.Active), StringComparison.Ordinal))
        {
            RecomputeCapabilityAssignments();
        }
    }

    private void PublishLoadedModule(ModuleRuntimeInfo module)
    {
        bool isModuleLoaded = false;
        for (int i = 0; i < LoadedModulesMetadata.Count; i++)
        {
            if (LoadedModulesMetadata[i].ModulePath != null &&
                string.Equals(LoadedModulesMetadata[i].ModulePath, module.ModuleInformation.ModulePath, StringComparison.Ordinal))
            {
                LoadedModulesMetadata[i] = module.ModuleInformation;
                isModuleLoaded = true;
                break;
            }
        }

        if (!isModuleLoaded)
        {
            LoadedModulesMetadata.Add(module.ModuleInformation);
        }

        if (_availableSandboxModules.Count == 0)
        {
            LogNoModulesLoaded(_logger);
            ShowNoModulesPlaceholder();
            return;
        }

        if (LoadedModulesMetadata.Count > 0 && LoadedModulesMetadata[0].IsPlaceholder)
        {
            LoadedModulesMetadata.RemoveAt(0);
        }

        if (module.ModuleInformation.Active)
        {
            LogTrackingInitializedVia(_logger, module.ModuleClassName);
        }
    }

    private void HandleReplyUpdate(ReplyUpdatePacket reply, ModuleRuntimeInfo module)
    {
        if (module.Status != ModuleState.Active || !module.ModuleInformation.Active)
        {
            return;
        }

        TrackingCapability capabilities = module.ModuleInformation.EffectiveCapabilities;
        bool accepted = module.TryAcceptSample(reply.SampleTicks, capabilities);
        if (accepted)
        {
            using (UnifiedTracking.LockData())
            {
                reply.UpdateGlobalState(capabilities);
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _ipcLatency.Record(reply, accepted);
        }
    }

    private static void HandleDebugStreamFrame(ImageFrameUpdatePacket framePacket, ModuleRuntimeInfo module)
    {
        if (module.Status != ModuleState.Active || !module.ModuleInformation.Active)
        {
            return;
        }

        var frame = new Image
        {
            ImageSize = (framePacket.Width, framePacket.Height),
            ImageData = framePacket.GetData(),
            SupportsImage = true,
        };
        TrackingCapability capabilities = module.ModuleInformation.EffectiveCapabilities;
        if (framePacket.Kind == ImageFrameUpdatePacket.EyeKind && capabilities.Allows(TrackingCapability.Eyes))
        {
            UnifiedTracking.EyeImageData = frame;
        }
        else if (framePacket.Kind == ImageFrameUpdatePacket.LipKind && capabilities.Allows(TrackingCapability.Mouth))
        {
            UnifiedTracking.LipImageData = frame;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring invalid handshake from port {Port}")]
    private static partial void LogIgnoringInvalidHandshakePort(ILogger logger, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing {Module}...")]
    private static partial void LogInitializing(ILogger logger, string module);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring handshake from port {Port}: no process with PID {Pid}")]
    private static partial void LogIgnoringHandshakePortNoProcess(ILogger logger, int port, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring {Type} packet from unregistered port {Port}")]
    private static partial void LogIgnoringPacketUnregisteredPort(ILogger logger, IpcPacket.PacketType type, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Got supported for module {Module}. Expr: {ExpressionAvailable} Eye: {EyeAvailable}...")]
    private static partial void LogGotSupportedModuleExprEye(ILogger logger, string module, bool expressionAvailable, bool eyeAvailable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Got init for module {Module}. Eye: {Eye} Expr: {Expr}...")]
    private static partial void LogGotInitModuleEyeExpr(ILogger logger, string module, bool eye, bool expr);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking initialized via {Module}")]
    private static partial void LogTrackingInitializedVia(ILogger logger, string module);

    [LoggerMessage(Message = "{Message}")]
    private static partial void LogModuleMessage(ILogger logger, LogLevel level, string message);
}
