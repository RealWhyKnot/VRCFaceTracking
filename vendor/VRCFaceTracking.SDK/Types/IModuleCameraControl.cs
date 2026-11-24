namespace VRCFaceTracking.Core.Types;

public interface IModuleCameraControl
{
    CameraControlCapabilities SupportedCameraControls { get; }

    void ApplyCameraControl(CameraControlCommand command);
}
