namespace VRCFaceTracking.Core.Types;

public sealed record CameraFrame(string StreamId, CameraStreamRole Role, string Label, int Width, int Height, CameraPixelFormat Format, byte[] Data);
