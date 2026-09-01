using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Sandboxing.IPC;

public class ReplyInitPacket : IpcPacket
{
    public bool eyeSuccess = false;
    public bool expressionSuccess = false;
    public string ModuleInformationName = "";
    public List<Stream> IconDataStreams = new();

    public override PacketType GetPacketType() => PacketType.ReplyInit;

    public override byte[] GetBytes()
    {
        // Build init packet
        var packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());

        var packedData = eyeSuccess ? 1 : 0;
        packedData |= (expressionSuccess ? 2 : 0);

        var moduleInfoStringData = Encoding.UTF8.GetBytes(ModuleInformationName);
        var moduleInfoSizeBytes = BitConverter.GetBytes(moduleInfoStringData.Length);

        var packedDataByte = (byte)packedData;

        var streamCountCorrected = IconDataStreams != null ? IconDataStreams.Count : 0;
        var streamCountBytes = BitConverter.GetBytes(streamCountCorrected);

        // Packet size
        var packetSize = SIZE_PACKET_MAGIC + SIZE_PACKET_TYPE + 1 + sizeof(int) + moduleInfoStringData.Length +
            sizeof(int); // Number of streams

        // Packing of the stream[]:
        // We can consider a single stream as a byte[].
        List<byte[]> streamDataBytes = new();

        for (var i = 0; i < streamCountCorrected; i++)
        {
            var currStreamData = ReadStreamDataEntirely(IconDataStreams[i]);
            streamDataBytes.Add(currStreamData);
            packetSize += sizeof(int);              // Stream Length
            packetSize += currStreamData.Length;    // Stream Data
        }

        // Prepare buffer
        var finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HANDSHAKE_MAGIC, 0, finalDataStream, 0, SIZE_PACKET_MAGIC);                    // Magic
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, SIZE_PACKET_TYPE);                     // Packet Type
        finalDataStream[8] = packedDataByte;                                                            // packedDataByte
        Buffer.BlockCopy(moduleInfoSizeBytes, 0, finalDataStream, 9, sizeof(int));                   // Length
        Buffer.BlockCopy(moduleInfoStringData, 0, finalDataStream, 13, moduleInfoStringData.Length);   // Data

        var offset = 13 + moduleInfoStringData.Length;

        Buffer.BlockCopy(streamCountBytes, 0, finalDataStream, offset, streamCountBytes.Length);   // Streams.Count

        offset += 4;

        for (var i = 0; i < streamDataBytes.Count; i++)
        {
            var streamCount = BitConverter.GetBytes(streamDataBytes[i].Length);
            Buffer.BlockCopy(streamCount, 0, finalDataStream, offset, streamCount.Length);                // Stream.Count
            Buffer.BlockCopy(streamDataBytes[i], 0, finalDataStream, offset + 4, streamDataBytes[i].Length);     // Stream.Data
            offset = offset + 4 + streamDataBytes[i].Length;
        }
        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {
        // Unpack data into booleans
        var packedData = data[8];
        this.eyeSuccess = (packedData & 1) == 1;
        this.expressionSuccess = (packedData & 2) == 2;

        var moduleInfoStringLength = BitConverter.ToInt32(data, 9);
        ModuleInformationName = Encoding.UTF8.GetString(data, 13, moduleInfoStringLength);

        var streamCount = BitConverter.ToInt32(data, 13 + moduleInfoStringLength);

        var offset = 13 + moduleInfoStringLength + 4;

        for (var i = 0; i < streamCount; i++)
        {
            var imageStreamSize = BitConverter.ToInt32(data, offset);

            MemoryStream imageStream = new MemoryStream();
            imageStream.Write(data, offset + 4, imageStreamSize);
            imageStream.Position = 0;

            IconDataStreams.Add(imageStream);
            offset += 4 + imageStreamSize;
        }
    }

    private static byte[] ReadStreamDataEntirely(Stream input)
    {
        using MemoryStream ms = new MemoryStream();
        input.CopyTo(ms);
        return ms.ToArray();
    }
}
