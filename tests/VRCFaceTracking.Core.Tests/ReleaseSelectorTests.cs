using VRCFaceTracking.Core.Updates;

namespace VRCFaceTracking.Core.Tests;

public sealed class ReleaseSelectorTests
{
    private static GithubRelease Release(string tag, bool prerelease = false, bool draft = false) =>
        new()
        {
            TagName = tag,
            Prerelease = prerelease,
            Draft = draft,
        };

    private static readonly AppVersion Current = new(2026, 9, 1, 0, IsBeta: true);

    [Fact]
    public void ReleaseChannelIgnoresPrereleaseAndDraft()
    {
        GithubRelease[] releases = [Release("v2026.9.3.0-beta", prerelease: true), Release("v2026.9.4.0", draft: true), Release("v2026.9.2.0")];
        Assert.Equal("v2026.9.2.0", ReleaseSelector.Select(releases, Current, BuildChannel.Release)?.TagName);
    }

    [Fact]
    public void BetaChannelPicksNewestAcrossBoth()
    {
        GithubRelease[] releases = [Release("v2026.9.2.0"), Release("v2026.9.3.0-beta", prerelease: true), Release("v2026.9.1.1-beta", prerelease: true)];
        Assert.Equal("v2026.9.3.0-beta", ReleaseSelector.Select(releases, Current, BuildChannel.Beta)?.TagName);
    }

    [Fact]
    public void StableSameNumbersBeatsCurrentBeta() => Assert.Equal("v2026.9.1.0", ReleaseSelector.Select([Release("v2026.9.1.0")], Current, BuildChannel.Beta)?.TagName);

    [Fact]
    public void NothingNewerReturnsNull()
    {
        GithubRelease[] releases = [Release("v2026.9.1.0-beta", prerelease: true), Release("v2026.8.1.0"), Release("garbage")];
        Assert.Null(ReleaseSelector.Select(releases, Current, BuildChannel.Beta));
    }

    [Fact]
    public void DevReturnsNull() => Assert.Null(ReleaseSelector.Select([Release("v2099.1.1.0")], Current, BuildChannel.Dev));
}
