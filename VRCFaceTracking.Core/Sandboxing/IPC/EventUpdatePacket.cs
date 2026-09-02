namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class EventUpdatePacket : IpcPacket
{
    private static readonly byte[] Encoded = Build();

    public override PacketType GetPacketType() => PacketType.EventUpdate;

    private static byte[] Build()
    {
        var data = new byte[SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, data, 0, SIZE_PACKET_MAGIC);
        BitConverter.TryWriteBytes(data.AsSpan(4), (uint)PacketType.EventUpdate);
        return data;
    }

    public override byte[] GetBytes() => Encoded;

    public override void Decode(in byte[] data)
    {
    }
}
