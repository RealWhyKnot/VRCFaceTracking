namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class ImageFrameUpdatePacket : IpcPacket
{
    public const byte EyeKind = 0;
    public const byte LipKind = 1;
    public const int HeaderSize = 21;
    public const int MaxDimension = 1024;
    public const int MaxFrameBytes = MaxDimension * MaxDimension * 4;

    public byte Kind;
    public int Width;
    public int Height;
    public byte[] Data = Array.Empty<byte>();

    public override PacketType GetPacketType() => PacketType.DebugStreamFrame;

    public override byte[] GetBytes()
    {
        var packet = new byte[HeaderSize + Data.Length];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, packet, 0, SIZE_PACKET_MAGIC);
        BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)GetPacketType());
        packet[8] = Kind;
        BitConverter.TryWriteBytes(packet.AsSpan(9), Width);
        BitConverter.TryWriteBytes(packet.AsSpan(13), Height);
        BitConverter.TryWriteBytes(packet.AsSpan(17), Data.Length);
        Buffer.BlockCopy(Data, 0, packet, HeaderSize, Data.Length);
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
        var length = BitConverter.ToInt32(data, 17);
        if (Kind > LipKind ||
            Width < 1 || Width > MaxDimension ||
            Height < 1 || Height > MaxDimension ||
            length != Width * Height * 4 ||
            data.Length < HeaderSize + length)
        {
            throw new InvalidDataException("Image frame packet is malformed");
        }

        Data = new byte[length];
        Buffer.BlockCopy(data, HeaderSize, Data, 0, length);
    }
}
