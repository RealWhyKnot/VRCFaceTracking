using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class ImageFrameV2Packet : IpcPacket
{
    public const int HeaderSize = 28;
    public const int MaxDimension = 2048;
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    private byte[] _data = [];

    public ImageFrameV2Packet()
    {
    }

    public ImageFrameV2Packet(string streamId, string label, CameraStreamRole role, CameraPixelFormat format, int width, int height, CameraControlCapabilities capabilities, byte[] data)
    {
        StreamId = streamId;
        Label = label;
        Role = role;
        Format = format;
        Width = width;
        Height = height;
        Capabilities = capabilities;
        _data = data;
    }

    public string StreamId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public CameraStreamRole Role { get; set; }
    public CameraPixelFormat Format { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public CameraControlCapabilities Capabilities { get; set; }

    public byte[] GetData() => _data;

    public static bool IsValidFrame(CameraPixelFormat format, int width, int height, int length)
    {
        bool inBounds = width >= 1 && width <= MaxDimension && height >= 1 && height <= MaxDimension && length >= 1 && length <= MaxFrameBytes;
        return inBounds && format switch
        {
            CameraPixelFormat.Rgba8888 or CameraPixelFormat.Bgra8888 => length == width * height * 4,
            CameraPixelFormat.Gray8 => length == width * height,
            CameraPixelFormat.Jpeg => true,
            _ => false,
        };
    }

    public override PacketType GetPacketType() => PacketType.DebugStreamFrameV2;

    public override byte[] GetBytes()
    {
        byte[] id = IpcText.Encode(StreamId);
        byte[] label = IpcText.Encode(Label);
        byte[] packet = new byte[HeaderSize + id.Length + label.Length + _data.Length];
        Buffer.BlockCopy(HandshakeMagic, 0, packet, 0, PacketMagicSize);
        BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)GetPacketType());
        packet[8] = (byte)Role;
        packet[9] = (byte)Format;
        BitConverter.TryWriteBytes(packet.AsSpan(10), Width);
        BitConverter.TryWriteBytes(packet.AsSpan(14), Height);
        BitConverter.TryWriteBytes(packet.AsSpan(18), (int)Capabilities);
        packet[22] = (byte)id.Length;
        packet[23] = (byte)label.Length;
        BitConverter.TryWriteBytes(packet.AsSpan(24), _data.Length);
        Buffer.BlockCopy(id, 0, packet, HeaderSize, id.Length);
        Buffer.BlockCopy(label, 0, packet, HeaderSize + id.Length, label.Length);
        Buffer.BlockCopy(_data, 0, packet, HeaderSize + id.Length + label.Length, _data.Length);
        return packet;
    }

    public override void Decode(in byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("Image frame packet is truncated");
        }

        var role = (CameraStreamRole)data[8];
        var format = (CameraPixelFormat)data[9];
        int width = BitConverter.ToInt32(data, 10);
        int height = BitConverter.ToInt32(data, 14);
        var capabilities = (CameraControlCapabilities)BitConverter.ToInt32(data, 18);
        int idLength = data[22];
        int labelLength = data[23];
        int length = BitConverter.ToInt32(data, 24);
        if (!Enum.IsDefined(role) || !Enum.IsDefined(format) || idLength < 1 ||
            !IsValidFrame(format, width, height, length) ||
            data.Length < HeaderSize + idLength + labelLength + length)
        {
            throw new InvalidDataException("Image frame packet is malformed");
        }

        StreamId = IpcText.Decode(data, HeaderSize, idLength);
        Label = IpcText.Decode(data, HeaderSize + idLength, labelLength);
        Role = role;
        Format = format;
        Width = width;
        Height = height;
        Capabilities = capabilities;
        _data = new byte[length];
        Buffer.BlockCopy(data, HeaderSize + idLength + labelLength, _data, 0, length);
    }
}
