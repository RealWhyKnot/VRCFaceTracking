namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class EncodedIpcPacket : IpcPacket
{
    private readonly PacketType _type;
    private readonly byte[] _bytes;

    public EncodedIpcPacket(PacketType type, byte[] bytes)
    {
        _type = type;
        _bytes = bytes;
    }

    public override PacketType GetPacketType() => _type;

    public override byte[] GetBytes() => _bytes;
}
