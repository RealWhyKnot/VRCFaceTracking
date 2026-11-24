using System.Text;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

internal static class IpcText
{
    public const int MaxBytes = 64;

    public static byte[] Encode(string? text)
    {
        string value = text ?? string.Empty;
        while (Encoding.UTF8.GetByteCount(value) > MaxBytes)
        {
            value = value[..^1];
        }

        return Encoding.UTF8.GetBytes(value);
    }

    public static string Decode(byte[] data, int offset, int length)
    {
        bool valid = length >= 0 && length <= MaxBytes && offset >= 0 && offset + length <= data.Length;
        return valid ? Encoding.UTF8.GetString(data, offset, length) : throw new InvalidDataException("Text field is malformed");
    }
}
