using System.Text;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Tests;

public class PacketDecoderTests
{
    private static readonly byte[] Magic = { 0xAF, 0xEC, 0x00, 0x8D };

    private static byte[] Header(IpcPacket.PacketType type)
    {
        var data = new byte[8];
        Buffer.BlockCopy(Magic, 0, data, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)type), 0, data, 4, 4);
        return data;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void TryDecodePacket_RejectsShortData(int length)
    {
        Assert.False(VrcftPacketDecoder.TryDecodePacket(new byte[length], out _));
    }

    [Fact]
    public void TryDecodePacket_RejectsBadMagic()
    {
        var data = Header(IpcPacket.PacketType.EventUpdate);
        data[0] = 0x00;
        Assert.False(VrcftPacketDecoder.TryDecodePacket(data, out _));
    }

    [Fact]
    public void TryDecodePacket_RejectsUnknownType()
    {
        Assert.False(VrcftPacketDecoder.TryDecodePacket(Header((IpcPacket.PacketType)12345), out _));
    }

    [Fact]
    public void TryDecodePacket_RejectsTruncatedBody()
    {
        var full = new EventLogPacket(Microsoft.Extensions.Logging.LogLevel.Information, "hello world").GetBytes();
        var truncated = full.Take(17).ToArray();
        Assert.False(VrcftPacketDecoder.TryDecodePacket(truncated, out _));
    }

    [Fact]
    public void TryDecodePacket_DecodesEventLogRoundTrip()
    {
        var bytes = new EventLogPacket(Microsoft.Extensions.Logging.LogLevel.Warning, "abc").GetBytes();
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out var packet));
        var log = Assert.IsType<EventLogPacket>(packet);
        Assert.Equal("abc", log.Message);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, log.LogLevel);
    }

    [Fact]
    public void Handshake_RoundTrips()
    {
        var sent = new HandshakePacket { ModulePath = @"C:\modules\test.dll" };
        var received = new HandshakePacket();
        received.Decode(sent.GetBytes());
        Assert.True(received.IsValid);
        Assert.Equal(Environment.ProcessId, received.PID);
        Assert.Equal(@"C:\modules\test.dll", received.ModulePath);
    }

    [Fact]
    public void Handshake_TruncatedIsInvalid()
    {
        var packet = new HandshakePacket();
        packet.Decode(Header(IpcPacket.PacketType.Handshake));
        Assert.False(packet.IsValid);
    }

    [Fact]
    public void Handshake_OversizedPathLengthIsInvalid()
    {
        var sent = new HandshakePacket { ModulePath = "x" };
        var data = sent.GetBytes();
        Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, data, 17, 4);
        var received = new HandshakePacket();
        received.Decode(data);
        Assert.False(received.IsValid);
    }

    [Fact]
    public void Handshake_BadChallengeIsInvalid()
    {
        var sent = new HandshakePacket { ModulePath = "x" };
        var data = sent.GetBytes();
        data[8] ^= 0xFF;
        var received = new HandshakePacket();
        received.Decode(data);
        Assert.False(received.IsValid);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(4)]
    public void ReplyUpdate_WrongStructSizeIsIgnored(int structSize)
    {
        var data = new byte[16];
        Buffer.BlockCopy(Header(IpcPacket.PacketType.ReplyUpdate), 0, data, 0, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(structSize), 0, data, 8, 4);
        var packet = new ReplyUpdatePacket();
        packet.Decode(data);
    }

    [Fact]
    public void PartialPacket_ShortChunkIsDiscarded()
    {
        PartialPacket.DecodePacket(new byte[10], out var packetData);
        Assert.Empty(packetData);
    }

    [Fact]
    public void PartialPacket_SplitAndReassembleRoundTrips()
    {
        var payload = Encoding.UTF8.GetBytes(new string('a', 5000));
        var chunks = PartialPacket.SplitPacketIntoChunks(payload, 1500);
        Assert.True(chunks.Length > 1);

        var combined = Array.Empty<byte>();
        foreach (var chunk in chunks)
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }
        Assert.Equal(payload, combined);

        combined = Array.Empty<byte>();
        foreach (var chunk in chunks)
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }
        Assert.Equal(payload, combined);
    }
}
