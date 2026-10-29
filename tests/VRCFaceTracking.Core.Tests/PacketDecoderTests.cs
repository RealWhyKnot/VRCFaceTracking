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
}
