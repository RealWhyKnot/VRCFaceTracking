using System.Text;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class EventLogPacket : IpcPacket
{
    public EventLogPacket(LogLevel level = LogLevel.Information, string message = "")
    {
        LogLevel = level;
        Message = message;
    }

    public string Message
    {
        get; private set;
    }
    public LogLevel LogLevel
    {
        get; private set;
    }

    public override PacketType GetPacketType() => PacketType.EventLog;

    public override byte[] GetBytes()
    {
        byte[] packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());
        byte[] logMessageStringData = Encoding.UTF8.GetBytes(Message);
        byte[] logMessageSizeBytes = BitConverter.GetBytes(logMessageStringData.Length);
        byte[] logLevel = BitConverter.GetBytes((int)LogLevel);

        int packetSize = PacketMagicSize + PacketTypeSize + sizeof(int) + sizeof(int) + logMessageStringData.Length;

        byte[] finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HandshakeMagic, 0, finalDataStream, 0, PacketMagicSize);
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, PacketTypeSize);
        Buffer.BlockCopy(logLevel, 0, finalDataStream, 8, sizeof(int));
        Buffer.BlockCopy(logMessageSizeBytes, 0, finalDataStream, 12, sizeof(int));
        Buffer.BlockCopy(logMessageStringData, 0, finalDataStream, 16, logMessageStringData.Length);

        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {
        LogLevel = (LogLevel)BitConverter.ToInt32(data, 8);
        int stringLength = BitConverter.ToInt32(data, 12);
        Message = Encoding.UTF8.GetString(data, 16, stringLength);
    }
}
