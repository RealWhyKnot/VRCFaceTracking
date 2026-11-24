using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Tests;

internal static class CameraTestData
{
    public static readonly Guid Module = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public static CameraStreamSnapshot Snap(
        string id = "left",
        CameraPixelFormat format = CameraPixelFormat.Rgba8888,
        int width = 64,
        int height = 64,
        long sequence = 1,
        CameraControlCapabilities capabilities = CameraControlCapabilities.None,
        byte[]? data = null,
        Guid? module = null,
        string? label = null)
    {
        int size = format switch
        {
            CameraPixelFormat.Gray8 => width * height,
            CameraPixelFormat.Jpeg => 16,
            CameraPixelFormat.Rgba8888 or CameraPixelFormat.Bgra8888 => width * height * 4,
            _ => 0,
        };
        return new CameraStreamSnapshot(
            module ?? Module,
            "Test module",
            id,
            label ?? id,
            CameraStreamRole.Other,
            format,
            width,
            height,
            capabilities,
            data ?? new byte[size],
            sequence,
            TimestampTicks: 0);
    }
}
