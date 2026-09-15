using System.ComponentModel;
using System.Diagnostics;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Library;

public class ModuleRuntimeInfo
{
    public CancellationTokenSource UpdateCancellationToken;
    public Thread UpdateThread;

    /// <summary>
    /// Whether the module is active and will receive update events.
    /// </summary>
    public bool IsActive;
    /// <summary>
    /// The UDP port the sandbox process associated with this application is on
    /// </summary>
    public int SandboxProcessPort;
    /// <summary>
    /// The PID of a sandbox process
    /// </summary>
    public int SandboxProcessPID;
    /// <summary>
    /// The path to the module the sandbox shall load
    /// </summary>
    public string SandboxModulePath;
    /// <summary>
    /// The process hosting the sandboxed module
    /// </summary>
    public Process Process;
    public ModuleProcessWatcher Watcher;
    public volatile bool TeardownRequested;
    /// <summary>
    /// The module's retreived metadata
    /// </summary>
    public ModuleMetadataInternal ModuleInformation;
    /// <summary>
    /// Module status
    /// </summary>
    public ModuleState Status = ModuleState.Uninitialized;
    /// <summary>
    /// The class name of the module. Retrieved through a metadata packet.
    /// </summary>
    public string ModuleClassName;

    /// <summary>
    /// Whether this module supports eye tracking or not.
    /// </summary>
    public bool SupportsEyeTracking;
    /// <summary>
    /// Whether this module supports expression tracking or not.
    /// </summary>
    public bool SupportsExpressionTracking;

    public bool EyeInitialized;
    public bool ExpressionInitialized;
    public int SpawnOrder;
    public PropertyChangedEventHandler StatusChangedHandler;

    /// <summary>
    /// Queue of packets to send
    /// </summary>
    public Queue<QueuedPacket> EventBus;
}

public struct QueuedPacket
{
    public IpcPacket packet;
    public int destinationPort;
}