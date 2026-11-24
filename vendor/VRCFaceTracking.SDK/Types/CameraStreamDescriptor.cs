namespace VRCFaceTracking.Core.Types;

public sealed record CameraStreamDescriptor(string Id, string Label, CameraStreamRole Role, CameraPixelFormat Format, int TargetFrameIntervalMs = 33);
