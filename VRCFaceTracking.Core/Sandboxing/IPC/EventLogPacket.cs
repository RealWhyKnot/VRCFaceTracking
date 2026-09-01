using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class EventLogPacket : IpcPacket
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
        // Build init packet
        var packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());
        var logMessageStringData = Encoding.UTF8.GetBytes(Message);
        var logMessageSizeBytes = BitConverter.GetBytes(logMessageStringData.Length);
        var logLevel = BitConverter.GetBytes((int)LogLevel);

        var packetSize = SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE + sizeof(int) + sizeof(int) + logMessageStringData.Length;

        // Prepare buffer
        var finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, finalDataStream, 0, SIZE_PACKET_MAGIC);              // Magic
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, SIZE_PACKET_TYPE);               // Packet Type
        Buffer.BlockCopy(logLevel, 0, finalDataStream, 8, sizeof(int));                    // Log level
        Buffer.BlockCopy(logMessageSizeBytes, 0, finalDataStream, 12, sizeof(int));                   // Length
        Buffer.BlockCopy(logMessageStringData, 0, finalDataStream, 16, logMessageStringData.Length);   // Data

        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {
        // Unpack data into booleans
        LogLevel = (LogLevel)BitConverter.ToInt32(data, 8);
        var stringLength = BitConverter.ToInt32(data, 12);
        Message = Encoding.UTF8.GetString(data, 16, stringLength);
    }
}
