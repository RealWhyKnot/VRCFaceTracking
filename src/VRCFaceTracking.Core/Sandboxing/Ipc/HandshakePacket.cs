using System.Text;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class HandshakePacket : IpcPacket
{

    private static readonly byte[] HandshakeChallenge = [0xDE, 0xC1, 0xCA, 0x12, 0XD0];

    public bool IsValid { get; private set; } = true;
    public int ProcessId { get; private set; }
    public string ModulePath
    {
        get; set;
    } = string.Empty;

    public override PacketType GetPacketType() => PacketType.Handshake;

    public HandshakePacket()
    {
        ProcessId = Environment.ProcessId;
    }

    public override byte[] GetBytes()
    {
        byte[] packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());
        byte[] pidBytes = BitConverter.GetBytes(ProcessId);
        byte[] modulePathData = Encoding.UTF8.GetBytes(ModulePath);
        byte[] modulePathLengthBytes = BitConverter.GetBytes(modulePathData.Length);

        int packetSize = PacketMagicSize + PacketTypeSize + HandshakeChallenge.Length + sizeof(int) + modulePathData.Length + sizeof(int);

        byte[] finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HandshakeMagic, 0, finalDataStream, 0, PacketMagicSize);
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, PacketTypeSize);
        Buffer.BlockCopy(HandshakeChallenge, 0, finalDataStream, 8, HandshakeChallenge.Length);
        Buffer.BlockCopy(pidBytes, 0, finalDataStream, 13, pidBytes.Length);
        Buffer.BlockCopy(modulePathLengthBytes, 0, finalDataStream, 17, modulePathLengthBytes.Length);
        Buffer.BlockCopy(modulePathData, 0, finalDataStream, 21, modulePathData.Length);

        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {
        if (data.Length < 21)
        {
            IsValid = false;
            return;
        }

        IsValid = true;
        for (int i = 0; i < HandshakeChallenge.Length; i++)
        {
            if (data[i + 8] != HandshakeChallenge[i])
            {
                IsValid = false;
                break;
            }
        }
        ProcessId = BitConverter.ToInt32(data, 13);

        int modulePathLength = BitConverter.ToInt32(data, 17);
        if (modulePathLength < 0 || modulePathLength > data.Length - 21)
        {
            IsValid = false;
            ModulePath = string.Empty;
            return;
        }
        ModulePath = Encoding.UTF8.GetString(data, 21, modulePathLength);
    }
}
