namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class ImageFrameUpdatePacket : IpcPacket
{
    public const byte EyeKind = 0;
    public const byte LipKind = 1;
    public const int HeaderSize = 21;
    public const int MaxDimension = 1024;
    public const int MaxFrameBytes = MaxDimension * MaxDimension * 4;

    private byte[] _data = [];

    public ImageFrameUpdatePacket()
    {
    }

    public ImageFrameUpdatePacket(byte kind, int width, int height, byte[] data)
    {
        Kind = kind;
        Width = width;
        Height = height;
        _data = data;
    }

    public byte Kind { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public byte[] GetData() => _data;

    public override PacketType GetPacketType() => PacketType.DebugStreamFrame;

    public override byte[] GetBytes()
    {
        byte[] packet = new byte[HeaderSize + _data.Length];
        Buffer.BlockCopy(HandshakeMagic, 0, packet, 0, PacketMagicSize);
        BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)GetPacketType());
        packet[8] = Kind;
        BitConverter.TryWriteBytes(packet.AsSpan(9), Width);
        BitConverter.TryWriteBytes(packet.AsSpan(13), Height);
        BitConverter.TryWriteBytes(packet.AsSpan(17), _data.Length);
        Buffer.BlockCopy(_data, 0, packet, HeaderSize, _data.Length);
        return packet;
    }

    public override void Decode(in byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("Image frame packet is truncated");
        }

        Kind = data[8];
        Width = BitConverter.ToInt32(data, 9);
        Height = BitConverter.ToInt32(data, 13);
        int length = BitConverter.ToInt32(data, 17);
        if (Kind > LipKind ||
            Width < 1 || Width > MaxDimension ||
            Height < 1 || Height > MaxDimension ||
            length != Width * Height * 4 ||
            data.Length < HeaderSize + length)
        {
            throw new InvalidDataException("Image frame packet is malformed");
        }

        _data = new byte[length];
        Buffer.BlockCopy(data, HeaderSize, _data, 0, length);
    }
}
