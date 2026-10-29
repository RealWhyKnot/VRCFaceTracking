using System.Text;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class ReplyInitPacket : IpcPacket
{
    public bool EyeSuccess { get; set; }
    public bool ExpressionSuccess { get; set; }
    public string ModuleInformationName { get; set; } = "";
    public IReadOnlyList<Stream> IconDataStreams { get; set; } = [];

    public override PacketType GetPacketType() => PacketType.ReplyInit;

    public override byte[] GetBytes()
    {
        byte[] packetTypeBytes = BitConverter.GetBytes((uint)GetPacketType());

        int packedData = EyeSuccess ? 1 : 0;
        packedData |= ExpressionSuccess ? 2 : 0;

        byte[] moduleInfoStringData = Encoding.UTF8.GetBytes(ModuleInformationName);
        byte[] moduleInfoSizeBytes = BitConverter.GetBytes(moduleInfoStringData.Length);

        byte packedDataByte = (byte)packedData;

        int streamCountCorrected = IconDataStreams.Count;
        byte[] streamCountBytes = BitConverter.GetBytes(streamCountCorrected);

        int packetSize = PacketMagicSize + PacketTypeSize + 1 + sizeof(int) + moduleInfoStringData.Length +
            sizeof(int);

        List<byte[]> streamDataBytes = [];

        for (int i = 0; i < streamCountCorrected; i++)
        {
            byte[] currStreamData = ReadStreamDataEntirely(IconDataStreams[i]);
            streamDataBytes.Add(currStreamData);
            packetSize += sizeof(int);
            packetSize += currStreamData.Length;
        }

        byte[] finalDataStream = new byte[packetSize];
        Buffer.BlockCopy(HandshakeMagic, 0, finalDataStream, 0, PacketMagicSize);
        Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, PacketTypeSize);
        finalDataStream[8] = packedDataByte;
        Buffer.BlockCopy(moduleInfoSizeBytes, 0, finalDataStream, 9, sizeof(int));
        Buffer.BlockCopy(moduleInfoStringData, 0, finalDataStream, 13, moduleInfoStringData.Length);

        int offset = 13 + moduleInfoStringData.Length;

        Buffer.BlockCopy(streamCountBytes, 0, finalDataStream, offset, streamCountBytes.Length);

        offset += 4;

        for (int i = 0; i < streamDataBytes.Count; i++)
        {
            byte[] streamCount = BitConverter.GetBytes(streamDataBytes[i].Length);
            Buffer.BlockCopy(streamCount, 0, finalDataStream, offset, streamCount.Length);
            Buffer.BlockCopy(streamDataBytes[i], 0, finalDataStream, offset + 4, streamDataBytes[i].Length);
            offset = offset + 4 + streamDataBytes[i].Length;
        }
        return finalDataStream;
    }

    public override void Decode(in byte[] data)
    {
        byte packedData = data[8];
        EyeSuccess = (packedData & 1) == 1;
        ExpressionSuccess = (packedData & 2) == 2;

        int moduleInfoStringLength = BitConverter.ToInt32(data, 9);
        ModuleInformationName = Encoding.UTF8.GetString(data, 13, moduleInfoStringLength);

        int streamCount = BitConverter.ToInt32(data, 13 + moduleInfoStringLength);

        int offset = 13 + moduleInfoStringLength + 4;
        if (streamCount < 0 || streamCount > (data.Length - offset) / 4)
        {
            throw new InvalidDataException("Init reply has more icon streams than bytes");
        }

        var streams = new List<Stream>(streamCount);
        for (int i = 0; i < streamCount; i++)
        {
            int imageStreamSize = BitConverter.ToInt32(data, offset);

            var imageStream = new MemoryStream();
            imageStream.Write(data, offset + 4, imageStreamSize);
            imageStream.Position = 0;

            streams.Add(imageStream);
            offset += 4 + imageStreamSize;
        }

        IconDataStreams = streams;
    }

    private static byte[] ReadStreamDataEntirely(Stream input)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return ms.ToArray();
    }
}
