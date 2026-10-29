using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace VRCFaceTracking.Core.Sandboxing.Ipc;

public sealed class PartialPacket : IpcPacket
{
    public override PacketType GetPacketType() => PacketType.SplitPacketChunk;

    private const uint PacketPartEofBit = 0x80000000;
    private const uint PacketNonDataSizeBytes = 16;
    private const uint PacketCountInvalid = 0xFFFFFFFF;

    internal const int MaxPendingPackets = 64;
    internal const long MaxPendingBytes = 64L * 1024 * 1024;
    internal const uint MaxChunksPerPacket = 4096;

    private static uint EncodePartialPacketCounter;
    private static readonly uint EncodePartialPacketBase =
        (uint)((Environment.ProcessId * Environment.ProcessId) + RandomNumberGenerator.GetInt32(0, 10000));
    private static readonly Dictionary<ulong, PartialPacketChunkTemporaryBuffer> PacketBuffers = [];
    private static readonly Lock BufferLock = new();
    private static long PendingBytes;

    private sealed class PartialPacketChunkTemporaryBuffer
    {
        public Dictionary<uint, byte[]> Chunks = [];
        public uint PacketCount = PacketCountInvalid;
        public long LastChunkTimestamp = Stopwatch.GetTimestamp();
        public long Bytes;
    }

    private static readonly TimeSpan StaleBufferLifetime = TimeSpan.FromSeconds(30);

    internal static int PendingPacketCount
    {
        get
        {
            lock (BufferLock)
            {
                return PacketBuffers.Count;
            }
        }
    }

    internal static long PendingByteCount
    {
        get
        {
            lock (BufferLock)
            {
                return PendingBytes;
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

        ulong packetId = ((ulong)(uint)senderKey << 32) | BitConverter.ToUInt32(data, 12);
        uint packetPart = BitConverter.ToUInt32(data, 8);
        uint part = packetPart & ~PacketPartEofBit;
        if (part >= MaxChunksPerPacket)
        {
            return;
        }

        byte[] chunk = data[(int)PacketNonDataSizeBytes..];

        lock (BufferLock)
        {
            PruneStaleBuffers();

            if (!PacketBuffers.TryGetValue(packetId, out PartialPacketChunkTemporaryBuffer? buffer))
            {
                buffer = new PartialPacketChunkTemporaryBuffer();
                PacketBuffers.Add(packetId, buffer);
            }

            if (!buffer.Chunks.TryAdd(part, chunk))
            {
                return;
            }

            buffer.Bytes += chunk.Length;
            PendingBytes += chunk.Length;
            buffer.LastChunkTimestamp = Stopwatch.GetTimestamp();
            if ((packetPart & PacketPartEofBit) == PacketPartEofBit)
            {
                buffer.PacketCount = part + 1;
            }

            EvictOverflow();
            if (!PacketBuffers.ContainsKey(packetId) || buffer.PacketCount != buffer.Chunks.Count)
            {
                return;
            }

            RemoveBuffer(packetId, buffer);
            if (TryCombine(buffer, out byte[]? combined) && !IsSplitChunk(combined))
            {
                packetData = combined;
            }
        }
    }

    private static void PruneStaleBuffers()
    {
        List<KeyValuePair<ulong, PartialPacketChunkTemporaryBuffer>>? stale = null;
        foreach (KeyValuePair<ulong, PartialPacketChunkTemporaryBuffer> pair in PacketBuffers)
        {
            if (Stopwatch.GetElapsedTime(pair.Value.LastChunkTimestamp) > StaleBufferLifetime)
            {
                (stale ??= []).Add(pair);
            }
        }

        if (stale != null)
        {
            foreach (KeyValuePair<ulong, PartialPacketChunkTemporaryBuffer> pair in stale)
            {
                RemoveBuffer(pair.Key, pair.Value);
            }
        }
    }

    private static void EvictOverflow()
    {
        while (PacketBuffers.Count > MaxPendingPackets || PendingBytes > MaxPendingBytes)
        {
            KeyValuePair<ulong, PartialPacketChunkTemporaryBuffer> oldest = PacketBuffers.MinBy(pair => pair.Value.LastChunkTimestamp);
            RemoveBuffer(oldest.Key, oldest.Value);
        }
    }

    private static void RemoveBuffer(ulong packetId, PartialPacketChunkTemporaryBuffer buffer)
    {
        if (PacketBuffers.Remove(packetId))
        {
            PendingBytes -= buffer.Bytes;
        }
    }

    private static bool TryCombine(PartialPacketChunkTemporaryBuffer buffer, [NotNullWhen(true)] out byte[]? packetData)
    {
        packetData = null;
        byte[][] ordered = new byte[buffer.PacketCount][];
        for (uint i = 0; i < buffer.PacketCount; i++)
        {
            if (!buffer.Chunks.TryGetValue(i, out byte[]? chunk))
            {
                return false;
            }

            ordered[i] = chunk;
        }

        packetData = new byte[buffer.Bytes];
        int copyOffset = 0;
        foreach (byte[] chunk in ordered)
        {
            Buffer.BlockCopy(chunk, 0, packetData, copyOffset, chunk.Length);
            copyOffset += chunk.Length;
        }

        return true;
    }

    private static bool IsSplitChunk(byte[] packet) =>
        packet.Length >= PacketMagicSize + PacketTypeSize
        && BitConverter.ToUInt32(packet, PacketMagicSize) == (uint)PacketType.SplitPacketChunk;

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
