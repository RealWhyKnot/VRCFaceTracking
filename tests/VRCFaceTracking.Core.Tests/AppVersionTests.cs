using VRCFaceTracking.Core.Updates;

namespace VRCFaceTracking.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("v2026.9.1.0-beta", 2026, 9, 1, 0, true)]
    [InlineData("2026.9.1.0", 2026, 9, 1, 0, false)]
    [InlineData("2026.9.1.0-EC3F", 2026, 9, 1, 0, false)]
    [InlineData(" V2026.12.31.7-BETA ", 2026, 12, 31, 7, true)]
    public void ParsesTagsAndVersionStrings(string text, int year, int month, int day, int revision, bool beta)
    {
        Assert.True(AppVersion.TryParse(text, out AppVersion version));
        Assert.Equal(new AppVersion(year, month, day, revision, beta), version);
    }

    [Theory]
    [InlineData("2026.9.1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2026.9.1.x")]
    [InlineData("2026.9.1.0.5")]
    public void RejectsMalformed(string? text) => Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void OrdersNumericThenStableOverBeta()
    {
        var stable = new AppVersion(2026, 9, 1, 0, IsBeta: false);
        var beta = new AppVersion(2026, 9, 1, 0, IsBeta: true);
        var older = new AppVersion(2026, 8, 31, 5, IsBeta: false);
        Assert.True(stable.CompareTo(beta) > 0);
        Assert.True(beta.CompareTo(older) > 0);
        Assert.True(older.CompareTo(stable) < 0);
        Assert.Equal(0, beta.CompareTo(new AppVersion(2026, 9, 1, 0, IsBeta: true)));
        Assert.True(new AppVersion(2026, 9, 1, 1, IsBeta: true).CompareTo(stable) > 0);
    }

    [Fact]
    public void ToStringRoundTrips()
    {
        Assert.Equal("2026.9.1.0-beta", new AppVersion(2026, 9, 1, 0, IsBeta: true).ToString());
        Assert.Equal("2026.9.1.0", new AppVersion(2026, 9, 1, 0, IsBeta: false).ToString());
    }
}
