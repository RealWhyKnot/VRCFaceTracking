using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Helpers;

internal static class CameraPixels
{
    public const int BytesPerPixel = 4;

    public static bool IsDrawable(CameraPixelFormat format, int width, int height, int length)
    {
        long pixels = (long)width * height;
        return width > 0 && height > 0 && format switch
        {
            CameraPixelFormat.Rgba8888 or CameraPixelFormat.Bgra8888 => length >= pixels * BytesPerPixel,
            CameraPixelFormat.Gray8 => length >= pixels,
            CameraPixelFormat.Jpeg => length > 0,
            _ => false,
        };
    }

    public static void ToBgra(CameraPixelFormat format, ReadOnlySpan<byte> source, int pixels, Span<byte> destination)
    {
        switch (format)
        {
            case CameraPixelFormat.Bgra8888:
                source[..(pixels * BytesPerPixel)].CopyTo(destination);
                break;
            case CameraPixelFormat.Rgba8888:
                for (int i = 0; i < pixels * BytesPerPixel; i += BytesPerPixel)
                {
                    destination[i] = source[i + 2];
                    destination[i + 1] = source[i + 1];
                    destination[i + 2] = source[i];
                    destination[i + 3] = source[i + 3];
                }

                break;
            case CameraPixelFormat.Gray8:
                for (int i = 0; i < pixels; i++)
                {
                    byte value = source[i];
                    int offset = i * BytesPerPixel;
                    destination[offset] = value;
                    destination[offset + 1] = value;
                    destination[offset + 2] = value;
                    destination[offset + 3] = 255;
                }

                break;
            case CameraPixelFormat.Jpeg:
            default:
                break;
        }
    }
}
