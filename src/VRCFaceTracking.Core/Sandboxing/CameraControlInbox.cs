using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed partial class CameraControlInbox
{
    public const int MaxPending = 32;

    private readonly ConcurrentQueue<CameraControlCommand> _pending = new();
    private int _faulted;

    public int Count => _pending.Count;

    public bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    public static bool IsModuleFault(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    public bool Enqueue(CameraControlCommand command, IModuleCameraControl? module, ILogger? logger = null)
    {
        if (module == null || IsFaulted || _pending.Count >= MaxPending || !IsSupported(SupportedBy(module, logger), command.Kind))
        {
            return false;
        }

        _pending.Enqueue(command);
        return true;
    }

    public CameraControlCapabilities SupportedBy(IModuleCameraControl? module, ILogger? logger = null)
    {
        if (module == null || IsFaulted)
        {
            return CameraControlCapabilities.None;
        }

        try
        {
            return module.SupportedCameraControls;
        }
        catch (Exception ex) when (IsModuleFault(ex))
        {
            Fault(module, nameof(IModuleCameraControl.SupportedCameraControls), ex, logger);
            return CameraControlCapabilities.None;
        }
    }

    public int Drain(IModuleCameraControl module, ILogger? logger = null)
    {
        int applied = 0;
        while (!IsFaulted && _pending.TryDequeue(out CameraControlCommand? command))
        {
            try
            {
                module.ApplyCameraControl(command);
                applied++;
            }
            catch (Exception ex) when (IsModuleFault(ex))
            {
                Fault(module, nameof(IModuleCameraControl.ApplyCameraControl), ex, logger);
            }
        }

        return applied;
    }

    public static bool IsSupported(CameraControlCapabilities capabilities, CameraControlKind kind) => kind switch
    {
        CameraControlKind.Exposure => capabilities.HasFlag(CameraControlCapabilities.Exposure),
        CameraControlKind.Brightness => capabilities.HasFlag(CameraControlCapabilities.Brightness),
        CameraControlKind.Roi => capabilities.HasFlag(CameraControlCapabilities.Roi),
        CameraControlKind.Recalibrate => capabilities.HasFlag(CameraControlCapabilities.Recalibrate),
        CameraControlKind.Recenter => capabilities.HasFlag(CameraControlCapabilities.Recenter),
        _ => false,
    };

    private void Fault(IModuleCameraControl module, string call, Exception exception, ILogger? logger)
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0)
        {
            return;
        }

        _pending.Clear();
        LogCameraControlFailed(logger ?? NullLogger.Instance, exception, module.GetType().Name, call);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Module} threw from {Call}, so camera controls are off until the module restarts")]
    private static partial void LogCameraControlFailed(ILogger logger, Exception exception, string module, string call);
}
