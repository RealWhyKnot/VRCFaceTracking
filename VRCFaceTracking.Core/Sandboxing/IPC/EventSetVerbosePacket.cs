namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class EventSetVerbosePacket : IpcPacket
{
    public bool Verbose;

    public override PacketType GetPacketType() => PacketType.EventSetVerbose;

    public override byte[] GetBytes()
    {
        var data = new byte[SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE + 1];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, data, 0, SIZE_PACKET_MAGIC);
        BitConverter.TryWriteBytes(data.AsSpan(4), (uint)GetPacketType());
        data[8] = Verbose ? (byte)1 : (byte)0;
        return data;
    }

    public override void Decode(in byte[] data)
    {
        Verbose = data[8] != 0;
    }
}
