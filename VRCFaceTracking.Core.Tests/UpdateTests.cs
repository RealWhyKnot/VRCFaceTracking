using VRCFaceTracking.Core.Updates;

namespace VRCFaceTracking.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("v2026.9.1.0-beta", 2026, 9, 1, 0, true)]
    [InlineData("2026.9.1.0", 2026, 9, 1, 0, false)]
    [InlineData("2026.9.1.0-EC3F", 2026, 9, 1, 0, false)]
    [InlineData(" V2026.12.31.7-BETA ", 2026, 12, 31, 7, true)]
    public void ParsesTagsAndVersionStrings(string text, int year, int month, int day, int revision, bool beta)
    {
        Assert.True(AppVersion.TryParse(text, out var version));
        Assert.Equal(new AppVersion(year, month, day, revision, beta), version);
    }

    [Theory]
    [InlineData("2026.9.1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2026.9.1.x")]
    [InlineData("2026.9.1.0.5")]
    public void RejectsMalformed(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void OrdersNumericThenStableOverBeta()
    {
        var stable = new AppVersion(2026, 9, 1, 0, false);
        var beta = new AppVersion(2026, 9, 1, 0, true);
        var older = new AppVersion(2026, 8, 31, 5, false);
        Assert.True(stable.CompareTo(beta) > 0);
        Assert.True(beta.CompareTo(older) > 0);
        Assert.True(older.CompareTo(stable) < 0);
        Assert.Equal(0, beta.CompareTo(new AppVersion(2026, 9, 1, 0, true)));
        Assert.True(new AppVersion(2026, 9, 1, 1, true).CompareTo(stable) > 0);
    }

    [Fact]
    public void ToStringRoundTrips()
    {
        Assert.Equal("2026.9.1.0-beta", new AppVersion(2026, 9, 1, 0, true).ToString());
        Assert.Equal("2026.9.1.0", new AppVersion(2026, 9, 1, 0, false).ToString());
    }
}

public class ReleaseSelectorTests
{
    private static GithubRelease Release(string tag, bool prerelease = false, bool draft = false) =>
        new()
        {
            TagName = tag,
            Prerelease = prerelease,
            Draft = draft
        };

    private static readonly AppVersion Current = new(2026, 9, 1, 0, true);

    [Fact]
    public void ReleaseChannelIgnoresPrereleaseAndDraft()
    {
        var releases = new[] { Release("v2026.9.3.0-beta", true), Release("v2026.9.4.0", draft: true), Release("v2026.9.2.0") };
        Assert.Equal("v2026.9.2.0", ReleaseSelector.Select(releases, Current, BuildChannel.Release)?.TagName);
    }

    [Fact]
    public void BetaChannelPicksNewestAcrossBoth()
    {
        var releases = new[] { Release("v2026.9.2.0"), Release("v2026.9.3.0-beta", true), Release("v2026.9.1.1-beta", true) };
        Assert.Equal("v2026.9.3.0-beta", ReleaseSelector.Select(releases, Current, BuildChannel.Beta)?.TagName);
    }

    [Fact]
    public void StableSameNumbersBeatsCurrentBeta()
    {
        Assert.Equal("v2026.9.1.0", ReleaseSelector.Select(new[] { Release("v2026.9.1.0") }, Current, BuildChannel.Beta)?.TagName);
    }

    [Fact]
    public void NothingNewerReturnsNull()
    {
        var releases = new[] { Release("v2026.9.1.0-beta", true), Release("v2026.8.1.0"), Release("garbage") };
        Assert.Null(ReleaseSelector.Select(releases, Current, BuildChannel.Beta));
    }

    [Fact]
    public void DevReturnsNull()
    {
        Assert.Null(ReleaseSelector.Select(new[] { Release("v2099.1.1.0") }, Current, BuildChannel.Dev));
    }
}

public class UpdateAssetsTests
{
    [Fact]
    public void NamesFollowReleaseLayout()
    {
        Assert.Equal("VRCFaceTracking-2026.9.1.0-beta-win-x64.zip", UpdateAssets.ZipName("v2026.9.1.0-beta"));
        Assert.Equal("VRCFaceTracking-2026.9.1.0-win-x64.integrity.tsv", UpdateAssets.IntegrityName("v2026.9.1.0"));
    }

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ParsesFirstRow()
    {
        var tsv = $"{Hash.ToUpperInvariant()}\t96910285\tapp.zip\r\nffff\t1\taf-ZA/x.mui\r\n";
        var (sha, size) = UpdateAssets.ParseZipEntry(tsv, "app.zip");
        Assert.Equal(Hash, sha);
        Assert.Equal(96910285, size);
    }

    [Theory]
    [InlineData(Hash + "\t10\tother.zip")]
    [InlineData(Hash + "\t10")]
    [InlineData("zz" + "\t10\tapp.zip")]
    [InlineData(Hash + "\t-1\tapp.zip")]
    [InlineData("")]
    public void RejectsBadRows(string tsv)
    {
        Assert.Throws<InvalidDataException>(() => UpdateAssets.ParseZipEntry(tsv, "app.zip"));
    }

    [Fact]
    public void ResolvesPayloadRoot()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Throws<InvalidDataException>(() => UpdateAssets.ResolvePayloadRoot(root));
            var nested = Directory.CreateDirectory(Path.Combine(root, "inner")).FullName;
            File.WriteAllText(Path.Combine(nested, UpdateAssets.ExeName), "");
            Assert.Equal(nested, UpdateAssets.ResolvePayloadRoot(root));
            File.WriteAllText(Path.Combine(root, UpdateAssets.ExeName), "");
            Assert.Equal(root, UpdateAssets.ResolvePayloadRoot(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class UpdateHelperScriptTests
{
    [Fact]
    public void QuotesPathsAndUsesCrlf()
    {
        var script = UpdateHelperScript.Build(4242, @"C:\stage\extracted", @"C:\stage", @"C:\Tom's Apps\VRCFT", @"C:\Tom's Apps\VRCFT\VRCFaceTracking.exe", @"C:\logs\update.log");
        Assert.Contains("Wait-Process -Id 4242", script);
        Assert.Contains(@"'C:\Tom''s Apps\VRCFT'", script);
        Assert.Contains(@"Remove-Item -LiteralPath 'C:\stage' -Recurse -Force -ErrorAction SilentlyContinue", script);
        Assert.True(script.IndexOf("Start-Process", StringComparison.Ordinal) < script.IndexOf("Remove-Item", StringComparison.Ordinal));
        Assert.Contains(@"Start-Process -FilePath 'C:\Tom''s Apps\VRCFT\VRCFaceTracking.exe'", script);
        Assert.DoesNotContain("\n", script.Replace("\r\n", ""));
        Assert.All(script, c => Assert.True(c < 128));
        Assert.DoesNotContain("??", script);
        Assert.DoesNotContain("&&", script);
    }
}
