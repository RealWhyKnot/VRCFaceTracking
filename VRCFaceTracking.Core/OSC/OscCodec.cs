using System.Buffers.Binary;
using System.Text;

namespace VRCFaceTracking.Core.OSC;

public static class OscCodec
{
    public static int EncodeMessage(byte[] buffer, OscMessage message) =>
        TryEncodeMessage(buffer, message, out var written) ? written : -1;

    public static int EncodeBundle(byte[] buffer, IReadOnlyList<OscMessage> messages, ref int index)
    {
        var buf = buffer.AsSpan();
        if (buf.Length < 16)
        {
            return 0;
        }

        "#bundle\0"u8.CopyTo(buf);
        var now = DateTimeOffset.UtcNow;
        BinaryPrimitives.WriteUInt32BigEndian(buf[8..], (uint)now.ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt32BigEndian(buf[12..], (uint)(now.Millisecond * 4294967L));

        var pos = 16;
        while (index < messages.Count)
        {
            if (pos + 4 > buf.Length || !TryEncodeMessage(buf[(pos + 4)..], messages[index], out var written))
            {
                break;
            }

            BinaryPrimitives.WriteInt32BigEndian(buf[pos..], written);
            pos += 4 + written;
            index++;
        }

        return pos;
    }

    public static OscMessage TryParse(byte[] buffer, int length, ref int index)
    {
        length = Math.Min(length, buffer.Length);
        var pos = index;
        if (pos < 0
            || !TryReadString(buffer, length, ref pos, out var address)
            || !address.StartsWith('/')
            || !TryReadString(buffer, length, ref pos, out var tags)
            || tags.Length == 0
            || tags[0] != ',')
        {
            return null;
        }

        var values = new OscValue[tags.Length - 1];
        for (var i = 1; i < tags.Length; i++)
        {
            ref var value = ref values[i - 1];
            switch (tags[i])
            {
                case 'i':
                    if (pos + 4 > length)
                    {
                        return null;
                    }
                    value.Type = OscValueType.Int;
                    value.IntValue = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos));
                    pos += 4;
                    break;
                case 'f':
                    if (pos + 4 > length)
                    {
                        return null;
                    }
                    value.Type = OscValueType.Float;
                    value.FloatValue = BinaryPrimitives.ReadSingleBigEndian(buffer.AsSpan(pos));
                    pos += 4;
                    break;
                case 'T':
                    value.Type = OscValueType.Bool;
                    value.BoolValue = true;
                    break;
                case 'F':
                    value.Type = OscValueType.Bool;
                    value.BoolValue = false;
                    break;
                case 's':
                    if (!TryReadString(buffer, length, ref pos, out var str))
                    {
                        return null;
                    }
                    value.Type = OscValueType.String;
                    value.StringValue = str;
                    break;
                case '[':
                    value.Type = OscValueType.ArrayBegin;
                    break;
                case ']':
                    value.Type = OscValueType.ArrayEnd;
                    break;
                default:
                    return null;
            }
        }

        index = pos;
        return new OscMessage(address, values);
    }

    private static bool TryEncodeMessage(Span<byte> buffer, OscMessage message, out int written)
    {
        written = 0;
        var pos = 0;
        if (!TryWriteString(buffer, ref pos, message.Address))
        {
            return false;
        }

        var values = message.Values;
        var tagsPadded = Pad4(1 + values.Length);
        if (pos + tagsPadded > buffer.Length)
        {
            return false;
        }

        buffer[pos] = (byte)',';
        for (var i = 0; i < values.Length; i++)
        {
            buffer[pos + 1 + i] = values[i].Type switch
            {
                OscValueType.Int => (byte)'i',
                OscValueType.Float => (byte)'f',
                OscValueType.Bool => values[i].BoolValue ? (byte)'T' : (byte)'F',
                OscValueType.String => (byte)'s',
                OscValueType.ArrayBegin => (byte)'[',
                OscValueType.ArrayEnd => (byte)']',
                _ => (byte)0,
            };
        }
        buffer.Slice(pos + 1 + values.Length, tagsPadded - 1 - values.Length).Clear();
        pos += tagsPadded;

        foreach (var value in values)
        {
            switch (value.Type)
            {
                case OscValueType.Int:
                    if (pos + 4 > buffer.Length)
                    {
                        return false;
                    }
                    BinaryPrimitives.WriteInt32BigEndian(buffer[pos..], value.IntValue);
                    pos += 4;
                    break;
                case OscValueType.Float:
                    if (pos + 4 > buffer.Length)
                    {
                        return false;
                    }
                    BinaryPrimitives.WriteSingleBigEndian(buffer[pos..], value.FloatValue);
                    pos += 4;
                    break;
                case OscValueType.String:
                    if (!TryWriteString(buffer, ref pos, value.StringValue ?? string.Empty))
                    {
                        return false;
                    }
                    break;
            }
        }

        written = pos;
        return true;
    }

    private static bool TryWriteString(Span<byte> buffer, ref int pos, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var padded = Pad4(byteCount);
        if (pos + padded > buffer.Length)
        {
            return false;
        }

        Encoding.UTF8.GetBytes(value, buffer[pos..]);
        buffer.Slice(pos + byteCount, padded - byteCount).Clear();
        pos += padded;
        return true;
    }

    private static bool TryReadString(byte[] buffer, int length, ref int pos, out string value)
    {
        value = null;
        if (pos >= length)
        {
            return false;
        }

        var nul = Array.IndexOf(buffer, (byte)0, pos, length - pos);
        if (nul < 0)
        {
            return false;
        }

        var padded = Pad4(nul - pos);
        if (pos + padded > length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(buffer, pos, nul - pos);
        pos += padded;
        return true;
    }

    private static int Pad4(int length) => (length + 4) & ~3;
}
