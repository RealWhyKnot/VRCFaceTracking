using VRCFaceTracking.Core.Mdns;
using VRCFaceTracking.Core.Mdns.Parsing;
using VRCFaceTracking.Core.Mdns.Types;

namespace VRCFaceTracking.Core.Tests;

public sealed class MdnsParsingTests
{
    private static byte[] Header(ushort questions, ushort answers = 0, ushort authorities = 0, ushort additionals = 0) =>
    [
        0, 0, 0x84, 0,
        (byte)(questions >> 8), (byte)questions,
        (byte)(answers >> 8), (byte)answers,
        (byte)(authorities >> 8), (byte)authorities,
        (byte)(additionals >> 8), (byte)additionals,
    ];

    private static DnsPacket Parse(byte[] data)
    {
        using var reader = new BigReader(data);
        return new DnsPacket(reader);
    }

    [Fact]
    public void SerializedResponseParsesBack()
    {
        var packet = new DnsPacket
        {
            Answers = [new DnsResource(new PtrRecord { DomainLabels = ["VRChat-Client-ABC123", "_oscjson", "_tcp", "local"] }, ["_oscjson", "_tcp", "local"])],
            Additionals = [new DnsResource(new SrvRecord { Port = 9123, Target = ["VRChat-Client-ABC123", "local"] }, ["VRChat-Client-ABC123", "_oscjson", "_tcp", "local"])],
        };
        byte[] data = packet.Serialize();

        DnsPacket parsed = Parse(data);

        Assert.True(parsed.IsResponse);
        Assert.Equal(["_oscjson", "_tcp", "local"], Assert.Single(parsed.Answers).Labels);
        Assert.Equal(9123, Assert.IsType<SrvRecord>(Assert.Single(parsed.Additionals).Data).Port);
    }

    [Fact]
    public void NamePointerBackToAnEarlierNameIsFollowed()
    {
        byte[] data =
        [
            .. Header(questions: 2),
            5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0, 0, 1, 0, 1,
            4, (byte)'h', (byte)'o', (byte)'s', (byte)'t', 0xC0, 12, 0, 1, 0, 1,
        ];

        DnsPacket parsed = Parse(data);

        Assert.Equal(["host", "local"], parsed.Questions[1].Labels);
    }

    [Theory]
    [InlineData(0xC0, 12)]
    [InlineData(0xC0, 30)]
    [InlineData(0xFF, 0xFF)]
    public void NamePointerToItselfOrForwardIsRejected(byte high, byte low)
    {
        byte[] data = [.. Header(questions: 1), high, low, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.Throws<InvalidDataException>(() => Parse(data));
    }

    [Fact]
    public void NameWithThousandsOfLabelsIsRejectedWithoutRecursing()
    {
        var data = new List<byte>(Header(questions: 1));
        for (int i = 0; i < 30000; i++)
        {
            data.Add(1);
            data.Add((byte)'a');
        }

        data.AddRange([0, 0, 1, 0, 1]);

        Assert.Throws<InvalidDataException>(() => Parse([.. data]));
    }

    [Fact]
    public void RecordCountsLargerThanThePacketAreRejectedBeforeAllocating()
    {
        byte[] data = Header(questions: ushort.MaxValue, answers: ushort.MaxValue, authorities: ushort.MaxValue, additionals: ushort.MaxValue);

        Assert.Throws<InvalidDataException>(() => Parse(data));
    }

    [Fact]
    public void RandomBytesOnlyEverFailAsBadPackets()
    {
        for (int seed = 0; seed < 20000; seed++)
        {
            byte[] data = Noise.Bytes(seed, Noise.Int(seed, 600));
            if (seed % 2 == 0 && data.Length >= 12)
            {
                data[4] = 0;
                data[5] = (byte)(seed % 4);
                data[6] = 0;
                data[7] = (byte)(seed % 3);
            }

            Exception? error = Record.Exception(() => Parse(data));
            Assert.True(error is null or EndOfStreamException or InvalidDataException, $"Seed {seed}: {error}");
        }
    }
}
