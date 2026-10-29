using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Core.Tests;

public sealed class LogFileNamesTests
{
    [Fact]
    public void MainAndModuleNamesFollowPattern()
    {
        var ts = new DateTime(2026, 9, 1, 13, 5, 9);
        Assert.Equal("vrcft_2026-09-01_13-05-09.log", LogFileNames.Main(ts));
        Assert.Equal("vrcft_module_VirtualDesktop.FaceTracking_2026-09-01_13-05-09.log", LogFileNames.Module("VirtualDesktop.FaceTracking", ts));
    }

    [Theory]
    [InlineData("a b:c", "a_b_c")]
    [InlineData("///", "module")]
    [InlineData("__x__", "x")]
    public void SanitizeStripsInvalidCharacters(string input, string expected) => Assert.Equal(expected, LogFileNames.Sanitize(input));
}
