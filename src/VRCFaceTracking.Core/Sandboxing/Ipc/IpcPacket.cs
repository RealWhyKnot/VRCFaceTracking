namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public class IpcPacket
{
    internal static readonly byte[] HandshakeMagic = [0xAF, 0xEC, 0x00, 0x8D,];
    internal static int PacketMagicSize => HandshakeMagic.Length;
    internal static int PacketTypeSize => 4;
    public enum PacketType
    {
        Unknown = 0,
        Handshake = 1,
        SplitPacketChunk = 2,

        EventGetSupported = 200,
        EventInit = 201,
        EventTeardown = 202,
        EventUpdate = 203,
        EventUpdateStatus = 204,
        EventLog = 205,
        EventSetVerbose = 206,
        EventSetImageStream = 207,

        ReplyGetSupported = 300,
        ReplyInit = 301,
        ReplyUpdate = 302,
        ReplyTeardown = 303,

        DebugStreamFrame = 1000,

    }

    public virtual PacketType GetPacketType() => PacketType.Unknown;

    public virtual byte[] GetBytes()
    {
        byte[] packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());

        int packetSize = PacketMagicSize + PacketTypeSize;

        byte[] data = new byte[packetSize];
        Buffer.BlockCopy(HandshakeMagic, 0, data, 0, PacketMagicSize);
        Buffer.BlockCopy(packetTypeBytes, 0, data, 4, PacketTypeSize);

        return data;
    }
    public virtual void Decode(in byte[] data)
    {
    }
}
