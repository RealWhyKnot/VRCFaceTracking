namespace VRCFaceTracking.Core.Types;

public interface ICameraStreamProvider
{
    IReadOnlyList<CameraStreamDescriptor> GetCameraStreams();

    IReadOnlyList<CameraFrame> GetCameraFrames();
}
