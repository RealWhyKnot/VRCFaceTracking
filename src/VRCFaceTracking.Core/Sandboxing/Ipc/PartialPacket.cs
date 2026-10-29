using System.Diagnostics;
using System.Security.Cryptography;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class PartialPacket : IpcPacket
{
    public override PacketType GetPacketType() => PacketType.SplitPacketChunk;

    private const uint PacketPartEofBit = 0x80000000;
    private const uint PacketNonDataSizeBytes = 16;
    private const uint PacketCountInvalid = 0xFFFFFFFF;

    private static uint EncodePartialPacketCounter;
    private static readonly uint EncodePartialPacketBase =
        (uint)((Environment.ProcessId * Environment.ProcessId) + RandomNumberGenerator.GetInt32(0, 10000));
    private static readonly Dictionary<ulong, PartialPacketChunkTemporaryBuffer> PacketBuffers = [];

    private struct PartialPacketChunk
    {
        public byte[] Data;
        public uint PacketPart;
    }
    private sealed class PartialPacketChunkTemporaryBuffer
    {
        public List<PartialPacketChunk> Chunks = [];
        public uint PacketCount = PacketCountInvalid;
        public long LastChunkTimestamp = Stopwatch.GetTimestamp();
    }

    private static readonly TimeSpan StaleBufferLifetime = TimeSpan.FromSeconds(30);

    private static void PruneStaleBuffers()
    {
        List<ulong>? stale = null;
        foreach (KeyValuePair<ulong, PartialPacketChunkTemporaryBuffer> pair in PacketBuffers)
        {
            if (Stopwatch.GetElapsedTime(pair.Value.LastChunkTimestamp) > StaleBufferLifetime)
            {
                (stale ??= []).Add(pair.Key);
            }
        }
        if (stale != null)
        {
            foreach (ulong key in stale)
            {
                PacketBuffers.Remove(key);
            }
        }
    }

    public override byte[] GetBytes() => [];
    public override void Decode(in byte[] data)
    {
    }

    public static void DecodePacket(byte[] data, out byte[] packetData, int senderKey = 0)
    {
        packetData = [];

        if (data.Length < PacketNonDataSizeBytes)
        {
            return;
        }

        PruneStaleBuffers();

        ulong packetId = ((ulong)(uint)senderKey << 32) | BitConverter.ToUInt32(data, 12);
        uint packetPart = BitConverter.ToUInt32(data, 8);

        var packetChunk = new PartialPacketChunk()
        {
            PacketPart = packetPart & ~PacketPartEofBit,
            Data = new byte[data.Length - PacketNonDataSizeBytes],
        };

        Buffer.BlockCopy(data, (int)PacketNonDataSizeBytes, packetChunk.Data, 0, (int)(data.Length - PacketNonDataSizeBytes));

        if (!PacketBuffers.TryGetValue(packetId, out PartialPacketChunkTemporaryBuffer? buffer))
        {
            buffer = new PartialPacketChunkTemporaryBuffer();
            PacketBuffers.Add(packetId, buffer);
        }

        buffer.Chunks.Add(packetChunk);
        buffer.LastChunkTimestamp = Stopwatch.GetTimestamp();

        if ((packetPart & PacketPartEofBit) == PacketPartEofBit)
        {
            buffer.PacketCount = (packetPart & ~PacketPartEofBit) + 1;
        }

        if (buffer.PacketCount == PacketCountInvalid || buffer.PacketCount != buffer.Chunks.Count)
        {
            return;
        }

        packetData = Combine(buffer.Chunks);
        PacketBuffers.Remove(packetId);
    }

    private static byte[] Combine(List<PartialPacketChunk> chunks)
    {
        long computedPacketSize = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            computedPacketSize += chunks[i].Data.Length;
        }
        byte[] packetData = new byte[computedPacketSize];

        var ordered = new PartialPacketChunk[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
        {
            uint part = chunks[i].PacketPart;
            if (part < (uint)ordered.Length && ordered[part].Data == null)
            {
                ordered[part] = chunks[i];
            }
        }

        int copyOffset = 0;
        for (int i = 0; i < ordered.Length; i++)
        {
            byte[] chunkData = ordered[i].Data;
            if (chunkData == null)
            {
                continue;
            }

            Buffer.BlockCopy(chunkData, 0, packetData, copyOffset, chunkData.Length);
            copyOffset += chunkData.Length;
        }

        return packetData;
    }

    public static byte[][] SplitPacketIntoChunks(byte[] packetData, int mtu)
    {
        if (mtu > 128)
        {
            mtu -= 128;
        }
        List<byte[]> packetCollection = [];

        long partialPacketCount = (long)Math.Ceiling((double)packetData.Length / (mtu - PacketNonDataSizeBytes));

        uint packetId = EncodePartialPacketBase ^ Interlocked.Increment(ref EncodePartialPacketCounter);

        byte[] packetTypeBytes = BitConverter.GetBytes((uint)PacketType.SplitPacketChunk);
        byte[] packetIdBytes = BitConverter.GetBytes(packetId);

        int bytesLeftToPack = packetData.Length;
        int bytesSent = 0;

        for (uint i = 0; i < partialPacketCount; i++)
        {
            int packetSize = Math.Min((int)(bytesLeftToPack + PacketNonDataSizeBytes), mtu);
            int packetSizeNoOverhead = (int)Math.Max(packetSize - PacketNonDataSizeBytes, 0);

            uint packetPart = i;
            if (i == partialPacketCount - 1)
            {
                packetPart |= PacketPartEofBit;
            }
            byte[] packetPartBytes = BitConverter.GetBytes(packetPart);

            byte[] finalDataStream = new byte[packetSize];
            Buffer.BlockCopy(HandshakeMagic, 0, finalDataStream, 0, PacketMagicSize);
            Buffer.BlockCopy(packetTypeBytes, 0, finalDataStream, 4, PacketTypeSize);
            Buffer.BlockCopy(packetPartBytes, 0, finalDataStream, 8, sizeof(int));
            Buffer.BlockCopy(packetIdBytes, 0, finalDataStream, 12, sizeof(int));

            Buffer.BlockCopy(packetData, bytesSent, finalDataStream, 16, packetSizeNoOverhead);

            bytesSent += packetSizeNoOverhead;
            bytesLeftToPack -= packetSizeNoOverhead;

            packetCollection.Add(finalDataStream);
        }

        return [.. packetCollection];
    }
}
