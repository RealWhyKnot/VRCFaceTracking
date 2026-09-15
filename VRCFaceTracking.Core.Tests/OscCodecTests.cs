using VRCFaceTracking.Core.OSC;

namespace VRCFaceTracking.Core.Tests;

public class OscCodecTests
{
    private static OscValue F(float v) => new() { Type = OscValueType.Float, FloatValue = v };
    private static OscValue I(int v) => new() { Type = OscValueType.Int, IntValue = v };
    private static OscValue B(bool v) => new() { Type = OscValueType.Bool, BoolValue = v };
    private static OscValue S(string v) => new() { Type = OscValueType.String, StringValue = v };
    private static OscValue Marker(OscValueType t) => new() { Type = t };

    private static string EncodeHex(string address, params OscValue[] values)
    {
        var buffer = new byte[4096];
        var length = OscCodec.EncodeMessage(buffer, new OscMessage(address, values));
        Assert.True(length >= 0);
        return Convert.ToHexString(buffer, 0, length);
    }

    [Theory]
    [InlineData("2F746573742F666C6F6174002C6600003F000000", "/test/float")]
    [InlineData("2F6162002C6600003F000000", "/ab")]
    public void EncodeMessage_Float_MatchesNativeFixture(string expected, string address)
    {
        Assert.Equal(expected, EncodeHex(address, F(0.5f)));
    }

    [Fact]
    public void EncodeMessage_Int_MatchesNativeFixture()
    {
        Assert.Equal("2F6100002C6900000000002A", EncodeHex("/a", I(42)));
    }

    [Fact]
    public void EncodeMessage_Bool_MatchesNativeFixture()
    {
        Assert.Equal("2F622F74000000002C540000", EncodeHex("/b/t", B(true)));
        Assert.Equal("2F622F66000000002C460000", EncodeHex("/b/f", B(false)));
    }

    [Fact]
    public void EncodeMessage_MultiValue_MatchesNativeFixture()
    {
        Assert.Equal("2F6D00002C666669000000003F000000BFA000000000002A", EncodeHex("/m", F(0.5f), F(-1.25f), I(42)));
    }

    [Fact]
    public void EncodeMessage_ArrayMarkers_MatchNativeFixture()
    {
        Assert.Equal(
            "2F617272000000002C5B66665D0000003F000000BFA00000",
            EncodeHex("/arr", Marker(OscValueType.ArrayBegin), F(0.5f), F(-1.25f), Marker(OscValueType.ArrayEnd)));
    }

    [Fact]
    public void EncodeMessage_NoValues_MatchesNativeFixture()
    {
        Assert.Equal("2F6E76002C000000", EncodeHex("/nv"));
    }

    [Theory]
    [InlineData("2F7300002C73000061626300", "abc")]
    [InlineData("2F7300002C7300006162636400000000", "abcd")]
    [InlineData("2F7300002C73000068C3A96C6C6F0000", "héllo")]
    public void EncodeMessage_String_MatchesOscSpec(string expected, string value)
    {
        Assert.Equal(expected, EncodeHex("/s", S(value)));
    }

    [Fact]
    public void EncodeMessage_TooLargeForBuffer_ReturnsNegative()
    {
        var buffer = new byte[64];
        var length = OscCodec.EncodeMessage(buffer, new OscMessage("/" + new string('a', 100), new[] { F(1f) }));
        Assert.Equal(-1, length);
    }

    [Fact]
    public void TryParse_Float_RoundTrips()
    {
        var bytes = Convert.FromHexString("2F746573742F666C6F6174002C6600003F000000");
        var index = 0;
        var msg = OscCodec.TryParse(bytes, bytes.Length, ref index);
        Assert.NotNull(msg);
        Assert.Equal("/test/float", msg.Address);
        Assert.Equal(0.5f, msg.Value);
        Assert.Equal(20, index);
    }

    [Fact]
    public void TryParse_MultiValue_RoundTrips()
    {
        var bytes = Convert.FromHexString("2F6D00002C666669000000003F000000BFA000000000002A");
        var index = 0;
        var msg = OscCodec.TryParse(bytes, bytes.Length, ref index);
        Assert.NotNull(msg);
        Assert.Equal("/m", msg.Address);
        Assert.Equal(24, index);
        Assert.Equal(3, msg.Values.Length);
        Assert.Equal(0.5f, msg.Values[0].FloatValue);
        Assert.Equal(-1.25f, msg.Values[1].FloatValue);
        Assert.Equal(42, msg.Values[2].IntValue);
    }

    [Fact]
    public void TryParse_Bools_RoundTrip()
    {
        var index = 0;
        var t = OscCodec.TryParse(Convert.FromHexString("2F622F74000000002C540000"), 12, ref index);
        Assert.Equal(true, t.Value);
        index = 0;
        var f = OscCodec.TryParse(Convert.FromHexString("2F622F66000000002C460000"), 12, ref index);
        Assert.Equal(false, f.Value);
    }

    [Fact]
    public void TryParse_String_RoundTrips()
    {
        var bytes = Convert.FromHexString("2F7300002C73000068C3A96C6C6F0000");
        var index = 0;
        var msg = OscCodec.TryParse(bytes, bytes.Length, ref index);
        Assert.Equal("héllo", msg.Value);
        Assert.Equal(16, index);
    }

    [Fact]
    public void TryParse_ArrayMarkers_RoundTrip()
    {
        var bytes = Convert.FromHexString("2F617272000000002C5B66665D0000003F000000BFA00000");
        var index = 0;
        var msg = OscCodec.TryParse(bytes, bytes.Length, ref index);
        Assert.Equal(4, msg.Values.Length);
        Assert.Equal(OscValueType.ArrayBegin, msg.Values[0].Type);
        Assert.Equal(0.5f, msg.Values[1].FloatValue);
        Assert.Equal(OscValueType.ArrayEnd, msg.Values[3].Type);
    }

    [Fact]
    public void TryParse_NoValues_ReturnsEmptyMessage()
    {
        var bytes = Convert.FromHexString("2F6E76002C000000");
        var index = 0;
        var msg = OscCodec.TryParse(bytes, bytes.Length, ref index);
        Assert.NotNull(msg);
        Assert.Equal("/nv", msg.Address);
        Assert.Null(msg.Value);
        Assert.Equal(8, index);
    }

    [Theory]
    [InlineData("78797A00")]
    [InlineData("")]
    [InlineData("74657374000000002C6600003F000000")]
    [InlineData("2362756E646C65000000000000000000000000142F746573742F666C6F6174002C6600003F000000")]
    public void TryParse_RejectsInvalidInput(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        var index = 0;
        Assert.Null(OscCodec.TryParse(bytes, bytes.Length, ref index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void TryParse_SequentialMessages_AdvanceIndex()
    {
        var bytes = Convert.FromHexString("2F746573742F666C6F6174002C6600003F0000002F6100002C6900000000002A");
        var index = 0;
        var first = OscCodec.TryParse(bytes, bytes.Length, ref index);
        Assert.Equal("/test/float", first.Address);
        Assert.Equal(20, index);
        var second = OscCodec.TryParse(bytes, bytes.Length, ref index);
        Assert.Equal("/a", second.Address);
        Assert.Equal(32, index);
    }

    [Fact]
    public void EncodeBundle_MatchesNativeFixture()
    {
        var messages = new List<OscMessage>
        {
            new("/test/float", new[] { F(0.5f) }),
            new("/b/t", new[] { B(true) }),
            new("/a", new[] { I(42) }),
        };
        var buffer = new byte[4096];
        var index = 0;
        var length = OscCodec.EncodeBundle(buffer, messages, ref index);
        Assert.Equal(72, length);
        Assert.Equal(3, index);
        for (var i = 8; i < 16; i++)
        {
            buffer[i] = 0;
        }
        Assert.Equal(
            "2362756E646C65000000000000000000000000142F746573742F666C6F6174002C6600003F0000000000000C2F622F74000000002C5400000000000C2F6100002C6900000000002A",
            Convert.ToHexString(buffer, 0, length));
    }

    [Fact]
    public void EncodeBundle_ChunksLikeNative()
    {
        var messages = Enumerable.Range(0, 200)
            .Select(n => new OscMessage($"/avatar/parameters/LongParameterName{n:D3}", new[] { F(0.5f) }))
            .ToList();
        var buffer = new byte[4096];
        var index = 0;
        var passes = new List<(int Length, int Index)>();
        while (index < messages.Count)
        {
            var last = index;
            var length = OscCodec.EncodeBundle(buffer, messages, ref index);
            Assert.True(index > last);
            passes.Add((length, index));
        }
        Assert.Equal(new[] { (4072, 78), (4072, 156), (2304, 200) }, passes);
    }

    [Fact]
    public void EncodeBundle_MessageTooLarge_DoesNotAdvance()
    {
        var messages = new List<OscMessage> { new("/" + new string('a', 5000), new[] { F(1f) }) };
        var buffer = new byte[4096];
        var index = 0;
        var length = OscCodec.EncodeBundle(buffer, messages, ref index);
        Assert.Equal(16, length);
        Assert.Equal(0, index);
    }
}
