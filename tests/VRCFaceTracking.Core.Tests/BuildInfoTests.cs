using System.Reflection;

namespace VRCFaceTracking.Core.Tests;

public sealed class BuildInfoTests
{
    [Theory]
    [InlineData("dev", BuildChannel.Dev)]
    [InlineData("BETA", BuildChannel.Beta)]
    [InlineData("Release", BuildChannel.Release)]
    [InlineData("nightly", BuildChannel.Dev)]
    [InlineData("", BuildChannel.Dev)]
    [InlineData(null, BuildChannel.Dev)]
    public void ParseChannelFallsBackToDev(string? value, BuildChannel expected) => Assert.Equal(expected, BuildInfo.ParseChannel(value));

    [Fact]
    public void ChannelComesFromAssemblyMetadataAndDrivesVerboseForcing()
    {
        string? metadata = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => string.Equals(a.Key, BuildInfo.ChannelMetadataKey, StringComparison.Ordinal)).Value;
        Assert.Equal(BuildInfo.ParseChannel(metadata), BuildInfo.Channel);
        Assert.Equal(BuildInfo.Channel != BuildChannel.Release, BuildInfo.VerboseForced);
    }

    [Fact]
    public void VersionStringCarriesTheBuildStamp()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+(-[A-Za-z0-9]+)?$", BuildInfo.VersionString);
        Assert.StartsWith($"{BuildInfo.Version.Major}.{BuildInfo.Version.Minor}.{BuildInfo.Version.Build}.{BuildInfo.Version.Revision}", BuildInfo.VersionString, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderBlockNamesProcessVersionAndChannel()
    {
        string header = BuildInfo.HeaderBlock("Tests", new DateTimeOffset(2026, 10, 29, 18, 4, 5, TimeSpan.FromHours(-5)));
        Assert.Contains("Started:  2026-10-29T23:04:05Z", header, StringComparison.Ordinal);
        Assert.Contains("Tests", header, StringComparison.Ordinal);
        Assert.Contains(BuildInfo.VersionString, header, StringComparison.Ordinal);
        Assert.Contains($"({BuildInfo.ChannelName})", header, StringComparison.Ordinal);
        Assert.Contains($"PID {Environment.ProcessId}", header, StringComparison.Ordinal);
    }
}
