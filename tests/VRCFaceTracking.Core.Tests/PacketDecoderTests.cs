using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Tests;

[Collection("PartialPacketState")]
public sealed class PacketDecoderTests
{
    private static readonly byte[] Magic = [0xAF, 0xEC, 0x00, 0x8D];

    private static byte[] Header(IpcPacket.PacketType type)
    {
        byte[] data = new byte[8];
        Buffer.BlockCopy(Magic, 0, data, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)type), 0, data, 4, 4);
        return data;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void TryDecodePacketRejectsShortData(int length) => Assert.False(VrcftPacketDecoder.TryDecodePacket(new byte[length], out _));

    [Fact]
    public void TryDecodePacketRejectsBadMagic()
    {
        byte[] data = Header(IpcPacket.PacketType.EventUpdate);
        data[0] = 0x00;
        Assert.False(VrcftPacketDecoder.TryDecodePacket(data, out _));
    }

    [Fact]
    public void TryDecodePacketRejectsUnknownType() => Assert.False(VrcftPacketDecoder.TryDecodePacket(Header((IpcPacket.PacketType)12345), out _));

    [Fact]
    public void TryDecodePacketRejectsTruncatedBody()
    {
        byte[] full = new EventLogPacket(Microsoft.Extensions.Logging.LogLevel.Information, "hello world").GetBytes();
        byte[] truncated = [.. full.Take(17)];
        Assert.False(VrcftPacketDecoder.TryDecodePacket(truncated, out _));
    }

    [Fact]
    public void TryDecodePacketDecodesEventLogRoundTrip()
    {
        byte[] bytes = new EventLogPacket(Microsoft.Extensions.Logging.LogLevel.Warning, "abc").GetBytes();
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? packet));
        EventLogPacket log = Assert.IsType<EventLogPacket>(packet);
        Assert.Equal("abc", log.Message);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, log.LogLevel);
    }

    [Fact]
    public void HandshakeRoundTrips()
    {
        var sent = new HandshakePacket { ModulePath = @"C:\modules\test.dll" };
        var received = new HandshakePacket();
        received.Decode(sent.GetBytes());
        Assert.True(received.IsValid);
        Assert.Equal(Environment.ProcessId, received.ProcessId);
        Assert.Equal(@"C:\modules\test.dll", received.ModulePath);
    }

    [Fact]
    public void HandshakeTruncatedIsInvalid()
    {
        var packet = new HandshakePacket();
        packet.Decode(Header(IpcPacket.PacketType.Handshake));
        Assert.False(packet.IsValid);
    }

    [Fact]
    public void HandshakeOversizedPathLengthIsInvalid()
    {
        var sent = new HandshakePacket { ModulePath = "x" };
        byte[] data = sent.GetBytes();
        Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, data, 17, 4);
        var received = new HandshakePacket();
        received.Decode(data);
        Assert.False(received.IsValid);
    }

    [Fact]
    public void HandshakeBadChallengeIsInvalid()
    {
        var sent = new HandshakePacket { ModulePath = "x" };
        byte[] data = sent.GetBytes();
        data[8] ^= 0xFF;
        var received = new HandshakePacket();
        received.Decode(data);
        Assert.False(received.IsValid);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(4)]
    public void ReplyUpdateWrongStructSizeIsIgnored(int structSize)
    {
        byte[] data = new byte[16];
        Buffer.BlockCopy(Header(IpcPacket.PacketType.ReplyUpdate), 0, data, 0, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(structSize), 0, data, 8, 4);
        var packet = new ReplyUpdatePacket();
        packet.Decode(data);
    }

    [Fact]
    public void EventUpdateRoundTripsSentTicks()
    {
        var sent = new EventUpdatePacket { SentTicks = 1234567890123L };
        Assert.True(VrcftPacketDecoder.TryDecodePacket(sent.GetBytes(), out IpcPacket? packet));
        EventUpdatePacket received = Assert.IsType<EventUpdatePacket>(packet);
        Assert.Equal(1234567890123L, received.SentTicks);
    }

    [Fact]
    public void EventUpdateLegacyHeaderOnlyDecodesWithZeroTicks()
    {
        Assert.True(VrcftPacketDecoder.TryDecodePacket(Header(IpcPacket.PacketType.EventUpdate), out IpcPacket? packet));
        Assert.Equal(0, Assert.IsType<EventUpdatePacket>(packet).SentTicks);
    }

    [Fact]
    public void PartialPacketShortChunkIsDiscarded()
    {
        PartialPacket.DecodePacket(new byte[10], out byte[]? packetData);
        Assert.Empty(packetData);
    }

    [Fact]
    public void PartialPacketSplitAndReassembleRoundTrips()
    {
        byte[] payload = Encoding.UTF8.GetBytes(new string('a', 5000));
        byte[][] chunks = PartialPacket.SplitPacketIntoChunks(payload, 1500);
        Assert.True(chunks.Length > 1);

        byte[]? combined = [];
        foreach (byte[] chunk in chunks)
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }
        Assert.Equal(payload, combined);

        combined = [];
        foreach (byte[] chunk in chunks)
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }
        Assert.Equal(payload, combined);
    }

    [Theory]
    [InlineData(16 * 1024)]
    [InlineData(64 * 1024)]
    [InlineData(640 * 1024)]
    public void DecodePacketReassemblesOutOfOrderChunks(int payloadSize)
    {
        byte[] payload = new byte[payloadSize];
        RandomNumberGenerator.Fill(payload);

        byte[][] chunks = PartialPacket.SplitPacketIntoChunks(payload, 8192);
        Assert.True(chunks.Length > 1);

        byte[][] shuffled = [.. chunks.OrderBy(_ => Guid.NewGuid())];

        byte[]? combined = null;
        foreach (byte[]? chunk in shuffled)
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }

        Assert.Equal(payload, combined);
    }

    [Fact]
    public void SplitPacketIntoChunksConcurrentCallersNeverShareAPacketId()
    {
        const int Threads = 8;
        const int PerThread = 400;
        byte[] payload = new byte[4096];
        var ids = new ConcurrentBag<uint>();

        Parallel.For(0, Threads, _ =>
        {
            for (int i = 0; i < PerThread; i++)
            {
                byte[][] chunks = PartialPacket.SplitPacketIntoChunks(payload, 1024);
                ids.Add(BitConverter.ToUInt32(chunks[0], 12));
            }
        });

        Assert.Equal(Threads * PerThread, ids.Distinct().Count());
    }

    private const int FuzzSenderKey = 0x5EED;

    private static byte[] Chunk(uint packetId, uint part, bool eof, byte[] body)
    {
        byte[] data = [.. Header(IpcPacket.PacketType.SplitPacketChunk), 0, 0, 0, 0, 0, 0, 0, 0, .. body];
        BitConverter.TryWriteBytes(data.AsSpan(8), eof ? part | 0x80000000 : part);
        BitConverter.TryWriteBytes(data.AsSpan(12), packetId);
        return data;
    }

    private static byte[] Decode(byte[] chunk)
    {
        PartialPacket.DecodePacket(chunk, out byte[] packetData, FuzzSenderKey);
        return packetData;
    }

    [Fact]
    public void DuplicateChunksDoNotCompleteAPacketEarly()
    {
        Assert.Empty(Decode(Chunk(1, 0, eof: false, [1])));
        Assert.Empty(Decode(Chunk(1, 0, eof: false, [9])));
        Assert.Empty(Decode(Chunk(1, 2, eof: true, [3])));

        Assert.Equal([1, 2, 3], Decode(Chunk(1, 1, eof: false, [2])));
    }

    [Fact]
    public void EndMarkerBeforeAMissingPartDropsThePacket()
    {
        int pending = PartialPacket.PendingPacketCount;

        Assert.Empty(Decode(Chunk(2, 0, eof: false, [1])));
        Assert.Empty(Decode(Chunk(2, 5, eof: false, [6])));
        Assert.Empty(Decode(Chunk(2, 2, eof: true, [3])));

        Assert.True(PartialPacket.PendingPacketCount <= pending);
        Assert.Empty(Decode(Chunk(2, 1, eof: false, [2])));
    }

    [Theory]
    [InlineData(PartialPacket.MaxChunksPerPacket)]
    [InlineData(0x7FFFFFFFu)]
    public void PartsPastTheChunkLimitAreDropped(uint part)
    {
        int pending = PartialPacket.PendingPacketCount;

        Assert.Empty(Decode(Chunk(3, part, eof: true, [1])));
        Assert.Empty(Decode(Chunk(3, part, eof: false, [1])));

        Assert.Equal(pending, PartialPacket.PendingPacketCount);
    }

    [Fact]
    public void SplitInsideASplitIsDropped()
    {
        byte[] inner = Assert.Single(PartialPacket.SplitPacketIntoChunks([1, 2, 3, 4], 1500));
        byte[] outer = Assert.Single(PartialPacket.SplitPacketIntoChunks(inner, 1500));

        PartialPacket.DecodePacket(outer, out byte[] packetData);

        Assert.Empty(packetData);
    }

    [Fact]
    public void PendingPacketsStayCapped()
    {
        for (uint id = 0; id < 1000; id++)
        {
            Assert.Empty(Decode(Chunk(0x10000 + id, 0, eof: false, [1, 2, 3])));
            Assert.True(PartialPacket.PendingPacketCount <= PartialPacket.MaxPendingPackets);
        }

        byte[] payload = new byte[20000];
        RandomNumberGenerator.Fill(payload);
        byte[]? combined = null;
        foreach (byte[] chunk in PartialPacket.SplitPacketIntoChunks(payload, 1500))
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }

        Assert.Equal(payload, combined);
    }

    [Fact]
    public void PendingBytesStayCapped()
    {
        byte[] body = new byte[60000];
        for (uint part = 0; part < 1200; part++)
        {
            Assert.Empty(Decode(Chunk(4, part, eof: false, body)));
            Assert.True(PartialPacket.PendingByteCount <= PartialPacket.MaxPendingBytes);
        }

        Assert.Empty(Decode(Chunk(4, 1200, eof: true, body)));
        Assert.True(PartialPacket.PendingByteCount <= PartialPacket.MaxPendingBytes);
    }

    [Fact]
    public void ReplyInitWithMoreStreamsThanBytesIsRejected()
    {
        byte[] data = [.. Header(IpcPacket.PacketType.ReplyInit), 3, 0, 0, 0, 0, .. BitConverter.GetBytes(int.MaxValue)];

        Assert.False(VrcftPacketDecoder.TryDecodePacket(data, out _));
    }

    [Fact]
    public void RandomBodiesNeverThrowForAnyPacketType()
    {
        foreach (IpcPacket.PacketType type in Enum.GetValues<IpcPacket.PacketType>())
        {
            for (int seed = 0; seed < 2000; seed++)
            {
                byte[] data = [.. Header(type), .. Noise.Bytes(seed, Noise.Int(seed, 400))];

                Exception? error = Record.Exception(() => VrcftPacketDecoder.TryDecodePacket(data, out _));

                Assert.True(error is null, $"{type} seed {seed}: {error}");
            }
        }
    }
}
