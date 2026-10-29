namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class EventSetImageStreamPacket : IpcPacket
{
    public bool Enabled;

    public override PacketType GetPacketType() => PacketType.EventSetImageStream;

    public override byte[] GetBytes()
    {
        var data = new byte[SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE + 1];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, data, 0, SIZE_PACKET_MAGIC);
        BitConverter.TryWriteBytes(data.AsSpan(4), (uint)GetPacketType());
        data[8] = Enabled ? (byte)1 : (byte)0;
        return data;
    }

    public override void Decode(in byte[] data)
    {
        Enabled = data[8] != 0;
    }
}
