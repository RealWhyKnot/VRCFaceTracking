namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class EventUpdatePacket : IpcPacket
{
    private const int SentTicksOffset = 8;
    private readonly byte[] _encoded = new byte[SentTicksOffset + sizeof(long)];

    public long SentTicks { get; set; }

    public EventUpdatePacket()
    {
        Buffer.BlockCopy(HandshakeMagic, 0, _encoded, 0, PacketMagicSize);
        BitConverter.TryWriteBytes(_encoded.AsSpan(4), (uint)PacketType.EventUpdate);
    }

    public override PacketType GetPacketType() => PacketType.EventUpdate;

    public override byte[] GetBytes()
    {
        BitConverter.TryWriteBytes(_encoded.AsSpan(SentTicksOffset), SentTicks);
        return _encoded;
    }

    public override void Decode(in byte[] data) => SentTicks = data.Length >= SentTicksOffset + sizeof(long) ? BitConverter.ToInt64(data, SentTicksOffset) : 0;
}
