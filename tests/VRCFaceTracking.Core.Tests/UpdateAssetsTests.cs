using VRCFaceTracking.Core.Updates;

namespace VRCFaceTracking.Core.Tests;

public sealed class UpdateAssetsTests
{
    [Fact]
    public void NamesFollowReleaseLayout()
    {
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        string suffix = $"{os}-{arch}";
        string ext = OperatingSystem.IsWindows() ? ".zip" : ".tar.gz";
        Assert.Equal(suffix, UpdateAssets.RidSuffix);
        Assert.Equal($"VRCFaceTracking-2026.9.1.0-beta-{suffix}", UpdateAssets.BaseName("v2026.9.1.0-beta"));
        Assert.Equal($"VRCFaceTracking-2026.9.1.0-beta-{suffix}{ext}", UpdateAssets.ArchiveName("v2026.9.1.0-beta"));
        Assert.Equal($"VRCFaceTracking-2026.9.1.0-{suffix}.integrity.tsv", UpdateAssets.IntegrityName("v2026.9.1.0"));
        Assert.Equal(OperatingSystem.IsWindows() ? "VRCFaceTracking.exe" : "VRCFaceTracking", UpdateAssets.ExeName);
        Assert.Equal("VRCFaceTracking-Setup-2026.9.1.0-beta.exe", UpdateAssets.SetupName("v2026.9.1.0-beta"));
        Assert.Equal("VRCFaceTracking-Setup-2026.9.1.0.integrity.tsv", UpdateAssets.SetupIntegrityName("v2026.9.1.0"));
    }

    [Fact]
    public void InstalledOnlyWithUninstallerOnWindows()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.False(UpdateAssets.IsInstalled(root));
            File.WriteAllText(Path.Combine(root, UpdateAssets.UninstallerName), "");
            Assert.Equal(OperatingSystem.IsWindows(), UpdateAssets.IsInstalled(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ParsesFirstRow()
    {
        string tsv = $"{Hash.ToUpperInvariant()}\t96910285\tapp.zip\r\nffff\t1\taf-ZA/x.mui\r\n";
        (string? sha, long size) = UpdateAssets.ParseArchiveEntry(tsv, "app.zip");
        Assert.Equal(Hash, sha, ignoreCase: true);
        Assert.Equal(96910285, size);
    }

    [Theory]
    [InlineData(Hash + "\t10\tother.zip")]
    [InlineData(Hash + "\t10")]
    [InlineData("zz" + "\t10\tapp.zip")]
    [InlineData(Hash + "\t-1\tapp.zip")]
    [InlineData("")]
    public void RejectsBadRows(string tsv) => Assert.Throws<InvalidDataException>(() => UpdateAssets.ParseArchiveEntry(tsv, "app.zip"));

    [Fact]
    public void ResolvesPayloadRoot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Throws<InvalidDataException>(() => UpdateAssets.ResolvePayloadRoot(root));
            string nested = Directory.CreateDirectory(Path.Combine(root, "inner")).FullName;
            File.WriteAllText(Path.Combine(nested, UpdateAssets.ExeName), "");
            Assert.Equal(nested, UpdateAssets.ResolvePayloadRoot(root));
            File.WriteAllText(Path.Combine(root, UpdateAssets.ExeName), "");
            Assert.Equal(root, UpdateAssets.ResolvePayloadRoot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
