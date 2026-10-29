using System.Text;
using VRCFaceTracking.Core.Osc;

namespace VRCFaceTracking.Core.Tests;

public sealed class OscCodecTests
{
    private static OscValue F(float v) => new() { Type = OscValueType.Float, FloatValue = v };
    private static OscValue I(int v) => new() { Type = OscValueType.Int, IntValue = v };
    private static OscValue B(bool v) => new() { Type = OscValueType.Bool, BoolValue = v };
    private static OscValue S(string v) => new() { Type = OscValueType.String, StringValue = v };
    private static OscValue Marker(OscValueType t) => new() { Type = t };
    private static OscValue Blob(byte[] v) => new() { Type = OscValueType.Blob, BlobValue = v };

    private static string EncodeHex(string address, params OscValue[] values)
    {
        byte[] buffer = new byte[4096];
        int length = OscCodec.EncodeMessage(buffer, new OscMessage(address, values));
        Assert.True(length >= 0);
        return Convert.ToHexString(buffer, 0, length);
    }

    [Theory]
    [InlineData("2F746573742F666C6F6174002C6600003F000000", "/test/float")]
    [InlineData("2F6162002C6600003F000000", "/ab")]
    public void EncodeMessageFloatMatchesNativeFixture(string expected, string address) => Assert.Equal(expected, EncodeHex(address, F(0.5f)));

    [Fact]
    public void EncodeMessageIntMatchesNativeFixture() => Assert.Equal("2F6100002C6900000000002A", EncodeHex("/a", I(42)));

    [Fact]
    public void EncodeMessageBoolMatchesNativeFixture()
    {
        Assert.Equal("2F622F74000000002C540000", EncodeHex("/b/t", B(v: true)));
        Assert.Equal("2F622F66000000002C460000", EncodeHex("/b/f", B(v: false)));
    }

    [Fact]
    public void EncodeMessageMultiValueMatchesNativeFixture() => Assert.Equal("2F6D00002C666669000000003F000000BFA000000000002A", EncodeHex("/m", F(0.5f), F(-1.25f), I(42)));

    [Fact]
    public void EncodeMessageArrayMarkersMatchNativeFixture()
    {
        Assert.Equal(
            "2F617272000000002C5B66665D0000003F000000BFA00000",
            EncodeHex("/arr", Marker(OscValueType.ArrayBegin), F(0.5f), F(-1.25f), Marker(OscValueType.ArrayEnd)));
    }

    [Fact]
    public void EncodeMessageNoValuesMatchesNativeFixture() => Assert.Equal("2F6E76002C000000", EncodeHex("/nv"));

    [Theory]
    [InlineData("2F7300002C73000061626300", "abc")]
    [InlineData("2F7300002C7300006162636400000000", "abcd")]
    [InlineData("2F7300002C73000068C3A96C6C6F0000", "héllo")]
    public void EncodeMessageStringMatchesOscSpec(string expected, string value) => Assert.Equal(expected, EncodeHex("/s", S(value)));

    [Theory]
    [InlineData("2F6200002C62000000000000", "")]
    [InlineData("2F6200002C6200000000000301020300", "010203")]
    [InlineData("2F6200002C6200000000000401020304", "01020304")]
    public void EncodeMessageBlobMatchesOscSpec(string expected, string data) => Assert.Equal(expected, EncodeHex("/b", Blob(Convert.FromHexString(data))));

    [Theory]
    [InlineData("2F6200002C62000000000000", "")]
    [InlineData("2F6200002C6200000000000301020300", "010203")]
    [InlineData("2F6200002C6200000000000401020304", "01020304")]
    public void TryParseBlobRoundTrips(string hex, string data)
    {
        byte[] bytes = Convert.FromHexString(hex);
        int index = 0;
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? msg));
        Assert.Equal(OscValueType.Blob, Assert.Single(msg._values).Type);
        Assert.Equal(Convert.FromHexString(data), msg.Value);
        Assert.Equal(bytes.Length, index);
    }

    [Fact]
    public void EncodeMessageFaceTrackingBlobIsPaddedTo132Bytes()
    {
        byte[] buffer = new byte[4096];
        Assert.Equal(132, OscCodec.EncodeMessage(buffer, OscMessage.CreateBlob("/tracking/face/v1", new byte[102])));
    }

    [Fact]
    public void EncodeMessageTooLargeForBufferReturnsNegative()
    {
        byte[] buffer = new byte[64];
        int length = OscCodec.EncodeMessage(buffer, new OscMessage("/" + new string('a', 100), [F(1f)]));
        Assert.Equal(-1, length);
    }

    [Fact]
    public void TryParseFloatRoundTrips()
    {
        byte[] bytes = Convert.FromHexString("2F746573742F666C6F6174002C6600003F000000");
        int index = 0;
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? msg));
        Assert.Equal("/test/float", msg.Address);
        Assert.Equal(0.5f, msg.Value);
        Assert.Equal(20, index);
    }

    [Fact]
    public void TryParseMultiValueRoundTrips()
    {
        byte[] bytes = Convert.FromHexString("2F6D00002C666669000000003F000000BFA000000000002A");
        int index = 0;
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? msg));
        Assert.Equal("/m", msg.Address);
        Assert.Equal(24, index);
        Assert.Equal(3, msg._values.Length);
        Assert.Equal(0.5f, msg._values[0].FloatValue);
        Assert.Equal(-1.25f, msg._values[1].FloatValue);
        Assert.Equal(42, msg._values[2].IntValue);
    }

    [Fact]
    public void TryParseBoolsRoundTrip()
    {
        int index = 0;
        Assert.True(OscCodec.TryParse(Convert.FromHexString("2F622F74000000002C540000"), 12, ref index, out OscMessage? t));
        Assert.Equal(true, t.Value);
        index = 0;
        Assert.True(OscCodec.TryParse(Convert.FromHexString("2F622F66000000002C460000"), 12, ref index, out OscMessage? f));
        Assert.Equal(false, f.Value);
    }

    [Fact]
    public void TryParseStringRoundTrips()
    {
        byte[] bytes = Convert.FromHexString("2F7300002C73000068C3A96C6C6F0000");
        int index = 0;
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? msg));
        Assert.Equal("héllo", msg.Value);
        Assert.Equal(16, index);
    }

    [Fact]
    public void TryParseArrayMarkersRoundTrip()
    {
        byte[] bytes = Convert.FromHexString("2F617272000000002C5B66665D0000003F000000BFA00000");
        int index = 0;
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? msg));
        Assert.Equal(4, msg._values.Length);
        Assert.Equal(OscValueType.ArrayBegin, msg._values[0].Type);
        Assert.Equal(0.5f, msg._values[1].FloatValue);
        Assert.Equal(OscValueType.ArrayEnd, msg._values[3].Type);
    }

    [Fact]
    public void TryParseNoValuesReturnsEmptyMessage()
    {
        byte[] bytes = Convert.FromHexString("2F6E76002C000000");
        int index = 0;
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? msg));
        Assert.Equal("/nv", msg.Address);
        Assert.Null(msg.Value);
        Assert.Equal(8, index);
    }

    [Theory]
    [InlineData("78797A00")]
    [InlineData("")]
    [InlineData("74657374000000002C6600003F000000")]
    [InlineData("2362756E646C65000000000000000000000000142F746573742F666C6F6174002C6600003F000000")]
    [InlineData("2F6200002C6200000000000801020304")]
    [InlineData("2F6200002C620000FFFFFFFF")]
    [InlineData("2F6200002C620000000000")]
    [InlineData("2F6200002C62000000000003010203")]
    public void TryParseRejectsInvalidInput(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        int index = 0;
        Assert.False(OscCodec.TryParse(bytes, bytes.Length, ref index, out _));
        Assert.Equal(0, index);
    }

    [Fact]
    public void TryParseSequentialMessagesAdvanceIndex()
    {
        byte[] bytes = Convert.FromHexString("2F746573742F666C6F6174002C6600003F0000002F6100002C6900000000002A");
        int index = 0;
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? first));
        Assert.Equal("/test/float", first.Address);
        Assert.Equal(20, index);
        Assert.True(OscCodec.TryParse(bytes, bytes.Length, ref index, out OscMessage? second));
        Assert.Equal("/a", second.Address);
        Assert.Equal(32, index);
    }

    [Fact]
    public void EncodeBundleMatchesNativeFixture()
    {
        var messages = new List<OscMessage>
        {
            new("/test/float", [F(0.5f)]),
            new("/b/t", [B(v: true)]),
            new("/a", [I(42)]),
        };
        byte[] buffer = new byte[4096];
        int index = 0;
        int length = OscCodec.EncodeBundle(buffer, messages, ref index);
        Assert.Equal(72, length);
        Assert.Equal(3, index);
        for (int i = 8; i < 16; i++)
        {
            buffer[i] = 0;
        }
        Assert.Equal(
            "2362756E646C65000000000000000000000000142F746573742F666C6F6174002C6600003F0000000000000C2F622F74000000002C5400000000000C2F6100002C6900000000002A",
            Convert.ToHexString(buffer, 0, length));
    }

    [Fact]
    public void EncodeBundleChunksLikeNative()
    {
        var messages = Enumerable.Range(0, 200)
            .Select(n => new OscMessage($"/avatar/parameters/LongParameterName{n:D3}", [F(0.5f)]))
            .ToList();
        byte[] buffer = new byte[4096];
        int index = 0;
        var passes = new List<(int Length, int Index)>();
        while (index < messages.Count)
        {
            int last = index;
            int length = OscCodec.EncodeBundle(buffer, messages, ref index);
            Assert.True(index > last);
            passes.Add((length, index));
        }
        Assert.Equal(new[] { (4072, 78), (4072, 156), (2304, 200) }, passes);
    }

    [Fact]
    public void EncodeBundleMessageTooLargeDoesNotAdvance()
    {
        var messages = new List<OscMessage> { new("/" + new string('a', 5000), [F(1f)]) };
        byte[] buffer = new byte[4096];
        int index = 0;
        int length = OscCodec.EncodeBundle(buffer, messages, ref index);
        Assert.Equal(16, length);
        Assert.Equal(0, index);
    }

    [Theory]
    [InlineData("/avatar/change", "/avatar/change", true)]
    [InlineData("/avatar/change", "/avatar/parameters/v2/EyeX", false)]
    [InlineData("/avatar/change", "/avatar/chang", false)]
    [InlineData("/avatar/change", "/avatar/changed", false)]
    [InlineData("/vrcft/settings/forceRelevant", "/vrcft/settings/forceRelevant", true)]
    [InlineData("/vrcft/settings/forceRelevant", "/vrcft/settings/forceRelevantX", false)]
    public void AddressMatchesOnlyOnWholeAddress(string handled, string sent, bool expected)
    {
        byte[] buffer = new byte[4096];
        int written = OscCodec.EncodeMessage(buffer, new OscMessage(sent, [F(1f)]));
        Assert.True(written > 0);

        Assert.Equal(expected, OscCodec.AddressMatches(buffer.AsSpan(0, written), Encoding.UTF8.GetBytes(handled)));
    }

    [Fact]
    public void AddressMatchesRejectsDatagramShorterThanAddress()
    {
        byte[] address = Encoding.UTF8.GetBytes("/avatar/change");
        Assert.False(OscCodec.AddressMatches(Encoding.UTF8.GetBytes("/avatar"), address));
        Assert.False(OscCodec.AddressMatches([], address));
        Assert.False(OscCodec.AddressMatches(address, address));
    }
}
