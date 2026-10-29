using System.Text;

namespace VRCFaceTracking.Core.Mdns.Parsing;

internal static class BigWriter
{
    public static byte[] WriteUInt16(ushort value)
    {
        byte[] bytes = [(byte)(value >> 8), (byte)(value & 0xFF)];
        return bytes;
    }

    public static byte[] WriteUInt32(uint value)
    {
        byte[] bytes = [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)(value & 0xFF)];
        return bytes;
    }

    public static byte[] WriteDomainLabels(List<string> labels)
    {
        List<byte> bytes = [];
        foreach (string label in labels)
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.Add(0);
        return [.. bytes];
    }
}
