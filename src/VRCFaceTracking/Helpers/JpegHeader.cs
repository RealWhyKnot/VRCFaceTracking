namespace VRCFaceTracking.Helpers;

internal static class JpegHeader
{
    public static bool Fits(ReadOnlySpan<byte> data, int maxDimension) =>
        !TryReadSize(data, out int width, out int height)
        || (width >= 1 && width <= maxDimension && height >= 1 && height <= maxDimension);

    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        int i = 2;
        while (i < data.Length)
        {
            if (data[i] != 0xFF)
            {
                return false;
            }

            while (i < data.Length && data[i] == 0xFF)
            {
                i++;
            }

            if (i >= data.Length)
            {
                return false;
            }

            byte marker = data[i++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                continue;
            }

            if (marker == 0xDA || i + 2 > data.Length)
            {
                return false;
            }

            int length = (data[i] << 8) | data[i + 1];
            if (length < 2)
            {
                return false;
            }

            if (IsFrame(marker))
            {
                if (i + 7 > data.Length)
                {
                    return false;
                }

                height = (data[i + 3] << 8) | data[i + 4];
                width = (data[i + 5] << 8) | data[i + 6];
                return true;
            }

            i += length;
        }

        return false;
    }

    private static bool IsFrame(byte marker) =>
        marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF);
}
