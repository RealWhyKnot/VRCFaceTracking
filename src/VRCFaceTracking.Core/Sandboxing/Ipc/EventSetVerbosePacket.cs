namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class EventSetVerbosePacket : IpcPacket
{
    public bool Verbose { get; set; }

    public override PacketType GetPacketType() => PacketType.EventSetVerbose;

    public override byte[] GetBytes()
    {
        byte[] data = new byte[PacketMagicSize + PacketTypeSize + 1];
        Buffer.BlockCopy(HandshakeMagic, 0, data, 0, PacketMagicSize);
        BitConverter.TryWriteBytes(data.AsSpan(4), (uint)GetPacketType());
        data[8] = Verbose ? (byte)1 : (byte)0;
        return data;
    }

    public override void Decode(in byte[] data) => Verbose = data[8] != 0;
}
