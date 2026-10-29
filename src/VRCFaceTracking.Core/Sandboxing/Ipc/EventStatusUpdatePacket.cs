using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class EventStatusUpdatePacket : IpcPacket
{
    public ModuleState ModuleState { get; set; }
    public bool UsingEye { get; set; }
    public bool UsingExpression { get; set; }

    public override PacketType GetPacketType() => PacketType.EventUpdateStatus;

    public override byte[] GetBytes()
    {
        byte[] packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());
        byte[] moduleStatePacket = BitConverter.GetBytes((int)ModuleState);

        int packetSize = PacketMagicSize + PacketTypeSize + moduleStatePacket.Length + 2;

        byte[] finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HandshakeMagic, 0, finalDataStream, 0, PacketMagicSize);
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, PacketTypeSize);
        Buffer.BlockCopy(moduleStatePacket, 0, finalDataStream, 8, moduleStatePacket.Length);
        finalDataStream[12] = UsingEye ? (byte)1 : (byte)0;
        finalDataStream[13] = UsingExpression ? (byte)1 : (byte)0;

        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {
        ModuleState = (ModuleState)BitConverter.ToInt32(data, 8);
        UsingEye = data.Length > 12 && BitConverter.ToBoolean(data, 12);
        UsingExpression = data.Length > 13 && BitConverter.ToBoolean(data, 13);
    }
}
