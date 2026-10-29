namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class EventTeardownPacket : IpcPacket
{
    public override PacketType GetPacketType() => PacketType.EventTeardown;

    public override byte[] GetBytes()
    {
        byte[] packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());

        int packetSize = PacketMagicSize + PacketTypeSize;

        byte[] finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HandshakeMagic, 0, finalDataStream, 0, PacketMagicSize);
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, PacketTypeSize);

        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {

    }

}
