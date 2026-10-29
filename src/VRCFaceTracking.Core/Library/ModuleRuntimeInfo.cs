using System.ComponentModel;
using System.Diagnostics;

namespace VRCFaceTracking.Core.Library;

public sealed class ModuleRuntimeInfo
{
    public CancellationTokenSource? UpdateCancellationToken { get; set; }
    public Thread? UpdateThread { get; set; }

    public bool IsActive { get; set; }
    public int SandboxProcessPort { get; set; }
    public int SandboxProcessPID { get; set; }
    public required string SandboxModulePath { get; set; }
    public required Process Process { get; set; }
    public ModuleProcessWatcher? Watcher { get; set; }
    public bool TeardownRequested { get => Volatile.Read(ref field); set => Volatile.Write(ref field, value); }
    public required ModuleMetadataInternal ModuleInformation { get; set; }
    public ModuleState Status { get; set; } = ModuleState.Uninitialized;
    public required string ModuleClassName { get; set; }

    public bool SupportsEyeTracking { get; set; }
    public bool SupportsExpressionTracking { get; set; }

    public bool EyeInitialized { get; set; }
    public bool ExpressionInitialized { get; set; }
    public int SpawnOrder { get; set; }
    public PropertyChangedEventHandler? StatusChangedHandler { get; set; }

    public Queue<QueuedPacket> EventBus { get; } = new();

    private long _lastSampleTicks;
    private TrackingCapability _lastSampleCapabilities;

    public bool TryAcceptSample(long sampleTicks, TrackingCapability capabilities)
    {
        if (sampleTicks != 0 && (sampleTicks < _lastSampleTicks || (sampleTicks == _lastSampleTicks && capabilities == _lastSampleCapabilities)))
        {
            return false;
        }

        _lastSampleTicks = sampleTicks;
        _lastSampleCapabilities = capabilities;
        return true;
    }
}
