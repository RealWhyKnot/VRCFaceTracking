using System.Collections.Concurrent;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Sandboxing;

public sealed class CameraControlInbox
{
    public const int MaxPending = 32;

    private readonly ConcurrentQueue<CameraControlCommand> _pending = new();

    public int Count => _pending.Count;

    public bool Enqueue(CameraControlCommand command, IModuleCameraControl? module)
    {
        if (module == null || !IsSupported(module.SupportedCameraControls, command.Kind) || _pending.Count >= MaxPending)
        {
            return false;
        }

        _pending.Enqueue(command);
        return true;
    }

    public int Drain(IModuleCameraControl module)
    {
        int applied = 0;
        while (_pending.TryDequeue(out CameraControlCommand? command))
        {
            module.ApplyCameraControl(command);
            applied++;
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
}
