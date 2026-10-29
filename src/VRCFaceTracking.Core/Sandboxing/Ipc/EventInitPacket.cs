namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class EventInitPacket : IpcPacket
{
    public bool EyeAvailable { get; set; }
    public bool ExpressionAvailable { get; set; }

    public override PacketType GetPacketType() => PacketType.EventInit;

    public override byte[] GetBytes()
    {
        byte[] packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());

        int packetSize = PacketMagicSize + PacketTypeSize + 1;

        int packedData = EyeAvailable ? 1 : 0;
        packedData |= ExpressionAvailable ? 2 : 0;

        byte packedDataByte = (byte)packedData;

        byte[] finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HandshakeMagic, 0, finalDataStream, 0, PacketMagicSize);
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, PacketTypeSize);
        finalDataStream[8] = packedDataByte;

        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {
        byte packedData = data[8];
        EyeAvailable = (packedData & 1) == 1;
        ExpressionAvailable = (packedData & 2) == 2;
    }
}
