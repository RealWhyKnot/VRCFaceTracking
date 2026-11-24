using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Library;

public sealed record CameraStreamSnapshot(
    Guid ModuleId,
    string ModuleName,
    string StreamId,
    string Label,
    CameraStreamRole Role,
    CameraPixelFormat Format,
    int Width,
    int Height,
    CameraControlCapabilities Capabilities,
    ReadOnlyMemory<byte> Data,
    long Sequence,
    long TimestampTicks)
{
    public bool IsLegacy => string.Equals(StreamId, CameraStreamRegistry.LegacyEyeId, StringComparison.Ordinal)
        || string.Equals(StreamId, CameraStreamRegistry.LegacyLipId, StringComparison.Ordinal);
}
