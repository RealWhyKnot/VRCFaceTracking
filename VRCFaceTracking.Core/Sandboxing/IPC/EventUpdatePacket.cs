namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class EventUpdatePacket : IpcPacket
{
    private const int SentTicksOffset = 8;
    private readonly byte[] _encoded = new byte[SentTicksOffset + sizeof(long)];

    public long SentTicks;

    public EventUpdatePacket()
    {
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, _encoded, 0, SIZE_PACKET_MAGIC);
        BitConverter.TryWriteBytes(_encoded.AsSpan(4), (uint)PacketType.EventUpdate);
    }

    public override PacketType GetPacketType() => PacketType.EventUpdate;

    public override byte[] GetBytes()
    {
        BitConverter.TryWriteBytes(_encoded.AsSpan(SentTicksOffset), SentTicks);
        return _encoded;
    }

    public override void Decode(in byte[] data)
    {
        SentTicks = data.Length >= SentTicksOffset + sizeof(long) ? BitConverter.ToInt64(data, SentTicksOffset) : 0;
    }
}
