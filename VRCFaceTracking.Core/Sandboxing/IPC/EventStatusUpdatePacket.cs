using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class EventStatusUpdatePacket : IpcPacket
{
    public ModuleState ModuleState;
    public bool UsingEye;
    public bool UsingExpression;

    public override PacketType GetPacketType() => PacketType.EventUpdateStatus;

    public override byte[] GetBytes()
    {
        // Build init packet
        var packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());
        var moduleStatePacket = BitConverter.GetBytes((int)ModuleState);

        var packetSize = SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE + moduleStatePacket.Length + 2;

        // Prepare buffer
        var finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, finalDataStream, 0, SIZE_PACKET_MAGIC);          // Magic
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, SIZE_PACKET_TYPE);           // Packet Type
        Buffer.BlockCopy(moduleStatePacket, 0, finalDataStream, 8, moduleStatePacket.Length);   // Module State
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
