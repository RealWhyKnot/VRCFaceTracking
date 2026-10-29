using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace VRCFaceTracking.Core.Osc;

public static class OscCodec
{
    public const int BundleHeaderSize = 16;

    private const ulong ImmediateTimeTag = 1;

    public static int EncodeMessage(byte[] buffer, OscMessage message) =>
        TryEncodeMessage(buffer, message, out int written) ? written : -1;

    public static int EncodeBundle(byte[] buffer, IReadOnlyList<OscMessage> messages, ref int index)
    {
        Span<byte> buf = buffer.AsSpan();
        if (buf.Length < BundleHeaderSize)
        {
            return 0;
        }

        "#bundle\0"u8.CopyTo(buf);
        BinaryPrimitives.WriteUInt64BigEndian(buf[8..], ImmediateTimeTag);

        int pos = BundleHeaderSize;
        while (index < messages.Count)
        {
            if (pos + 4 > buf.Length || !TryEncodeMessage(buf[(pos + 4)..], messages[index], out int written))
            {
                break;
            }

            BinaryPrimitives.WriteInt32BigEndian(buf[pos..], written);
            pos += 4 + written;
            index++;
        }

        return pos;
    }

    public static bool AddressMatches(ReadOnlySpan<byte> datagram, ReadOnlySpan<byte> address) =>
        datagram.Length > address.Length
        && datagram[address.Length] == 0
        && datagram[..address.Length].SequenceEqual(address);

    public static bool IsBundle(ReadOnlySpan<byte> datagram) =>
        datagram.Length >= BundleHeaderSize && datagram.StartsWith("#bundle\0"u8);

    public static bool TryReadBundleElement(ReadOnlySpan<byte> buffer, ref int pos, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (pos < 0 || pos > buffer.Length - 4)
        {
            return false;
        }

        int size = BinaryPrimitives.ReadInt32BigEndian(buffer[pos..]);
        if (size <= 0 || size > buffer.Length - pos - 4)
        {
            return false;
        }

        start = pos + 4;
        end = start + size;
        pos = end;
        return true;
    }

    public static bool TryParse(byte[] buffer, int length, ref int index, [NotNullWhen(true)] out OscMessage? message)
    {
        message = null;
        length = Math.Min(length, buffer.Length);
        int pos = index;
        if (pos < 0
            || !TryReadString(buffer, length, ref pos, out string? address)
            || !address.StartsWith('/')
            || !TryReadString(buffer, length, ref pos, out string? tags)
            || tags.Length == 0
            || tags[0] != ',')
        {
            return false;
        }

        var values = new OscValue[tags.Length - 1];
        for (int i = 1; i < tags.Length; i++)
        {
            if (!TryReadValue(buffer, length, ref pos, tags[i], out values[i - 1]))
            {
                return false;
            }
        }

        index = pos;
        message = new OscMessage(address, values);
        return true;
    }

    private static bool TryReadValue(byte[] buffer, int length, ref int pos, char tag, out OscValue value)
    {
        value = default;
        switch (tag)
        {
            case 'i':
                if (pos + 4 > length)
                {
                    return false;
                }
                value.Type = OscValueType.Int;
                value.IntValue = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos));
                pos += 4;
                return true;
            case 'f':
                if (pos + 4 > length)
                {
                    return false;
                }
                value.Type = OscValueType.Float;
                value.FloatValue = BinaryPrimitives.ReadSingleBigEndian(buffer.AsSpan(pos));
                pos += 4;
                return true;
            case 'T':
            case 'F':
                value.Type = OscValueType.Bool;
                value.BoolValue = tag == 'T';
                return true;
            case 's':
            case 'S':
                if (!TryReadString(buffer, length, ref pos, out string? str))
                {
                    return false;
                }
                value.Type = OscValueType.String;
                value.StringValue = str;
                return true;
            case 'b':
                if (!TryReadBlob(buffer, length, ref pos, out byte[]? blob))
                {
                    return false;
                }
                value.Type = OscValueType.Blob;
                value.BlobValue = blob;
                return true;
            case '[':
                value.Type = OscValueType.ArrayBegin;
                return true;
            case ']':
                value.Type = OscValueType.ArrayEnd;
                return true;
            default:
                return TryReadExtendedValue(buffer, length, ref pos, tag, out value);
        }
    }

    private static bool TryReadExtendedValue(byte[] buffer, int length, ref int pos, char tag, out OscValue value)
    {
        value = default;
        switch (tag)
        {
            case 'h':
                if (pos + 8 > length)
                {
                    return false;
                }
                value.Type = OscValueType.Int;
                value.IntValue = (int)Math.Clamp(BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(pos)), int.MinValue, int.MaxValue);
                pos += 8;
                return true;
            case 'd':
                if (pos + 8 > length)
                {
                    return false;
                }
                value.Type = OscValueType.Float;
                value.FloatValue = (float)BinaryPrimitives.ReadDoubleBigEndian(buffer.AsSpan(pos));
                pos += 8;
                return true;
            case 'c':
            case 'r':
            case 'm':
                if (pos + 4 > length)
                {
                    return false;
                }
                value.Type = OscValueType.Int;
                value.IntValue = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos));
                pos += 4;
                return true;
            case 't':
                if (pos + 8 > length)
                {
                    return false;
                }
                value.Type = OscValueType.Null;
                pos += 8;
                return true;
            case 'N':
            case 'I':
                value.Type = OscValueType.Null;
                return true;
            default:
                return false;
        }
    }

    private static bool TryEncodeMessage(Span<byte> buffer, OscMessage message, out int written)
    {
        written = 0;
        int pos = 0;
        if (!TryWriteBytes(buffer, ref pos, message.AddressBytes))
        {
            return false;
        }

        OscValue[] values = message._values;
        int tagsPadded = Pad4(1 + values.Length);
        if (pos + tagsPadded > buffer.Length)
        {
            return false;
        }

        buffer[pos] = (byte)',';
        for (int i = 0; i < values.Length; i++)
        {
            buffer[pos + 1 + i] = TypeTag(in values[i]);
        }
        buffer.Slice(pos + 1 + values.Length, tagsPadded - 1 - values.Length).Clear();
        pos += tagsPadded;

        for (int i = 0; i < values.Length; i++)
        {
            if (!TryWriteValue(buffer, ref pos, in values[i]))
            {
                return false;
            }
        }

        written = pos;
        return true;
    }

    private static byte TypeTag(in OscValue value) => value.Type switch
    {
        OscValueType.Int => (byte)'i',
        OscValueType.Float => (byte)'f',
        OscValueType.Bool => value.BoolValue ? (byte)'T' : (byte)'F',
        OscValueType.String => (byte)'s',
        OscValueType.Blob => (byte)'b',
        OscValueType.ArrayBegin => (byte)'[',
        OscValueType.ArrayEnd => (byte)']',
        OscValueType.Null => (byte)'N',
        _ => (byte)'N',
    };

    private static bool TryWriteValue(Span<byte> buffer, ref int pos, in OscValue value)
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
                return true;
            case OscValueType.Float:
                if (pos + 4 > buffer.Length)
                {
                    return false;
                }
                BinaryPrimitives.WriteSingleBigEndian(buffer[pos..], float.IsFinite(value.FloatValue) ? value.FloatValue : 0f);
                pos += 4;
                return true;
            case OscValueType.String:
                return TryWriteString(buffer, ref pos, value.StringValue ?? string.Empty);
            case OscValueType.Blob:
                return TryWriteBlob(buffer, ref pos, value.BlobValue);
            case OscValueType.Null:
            case OscValueType.Bool:
            case OscValueType.ArrayBegin:
            case OscValueType.ArrayEnd:
            default:
                return true;
        }
    }

    private static bool TryWriteBlob(Span<byte> buffer, ref int pos, ReadOnlySpan<byte> value)
    {
        int padded = PadBlob(value.Length);
        if (pos + 4 + padded > buffer.Length)
        {
            return false;
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer[pos..], value.Length);
        pos += 4;
        value.CopyTo(buffer[pos..]);
        buffer.Slice(pos + value.Length, padded - value.Length).Clear();
        pos += padded;
        return true;
    }

    private static bool TryReadBlob(byte[] buffer, int length, ref int pos, [NotNullWhen(true)] out byte[]? value)
    {
        value = null;
        if (pos + 4 > length)
        {
            return false;
        }

        int size = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos));
        if (size < 0 || size > length - pos - 4 || pos + 4 + PadBlob(size) > length)
        {
            return false;
        }

        value = buffer.AsSpan(pos + 4, size).ToArray();
        pos += 4 + PadBlob(size);
        return true;
    }

    private static bool TryWriteBytes(Span<byte> buffer, ref int pos, ReadOnlySpan<byte> value)
    {
        int padded = Pad4(value.Length);
        if (pos + padded > buffer.Length)
        {
            return false;
        }

        value.CopyTo(buffer[pos..]);
        buffer.Slice(pos + value.Length, padded - value.Length).Clear();
        pos += padded;
        return true;
    }

    private static bool TryWriteString(Span<byte> buffer, ref int pos, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        int padded = Pad4(byteCount);
        if (pos + padded > buffer.Length)
        {
            return false;
        }

        Encoding.UTF8.GetBytes(value, buffer[pos..]);
        buffer.Slice(pos + byteCount, padded - byteCount).Clear();
        pos += padded;
        return true;
    }

    private static bool TryReadString(byte[] buffer, int length, ref int pos, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (pos >= length)
        {
            return false;
        }

        int nul = Array.IndexOf(buffer, (byte)0, pos, length - pos);
        if (nul < 0)
        {
            return false;
        }

        int padded = Pad4(nul - pos);
        if (pos + padded > length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(buffer, pos, nul - pos);
        pos += padded;
        return true;
    }

    private static int Pad4(int length) => (length + 4) & ~3;

    private static int PadBlob(int length) => (length + 3) & ~3;
}
