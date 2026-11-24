namespace VRCFaceTracking.Core.Types;

[Flags]
public enum CameraControlCapabilities
{
    None = 0,
    Exposure = 1 << 0,
    Brightness = 1 << 1,
    Roi = 1 << 2,
    Recalibrate = 1 << 3,
    Recenter = 1 << 4,
}
