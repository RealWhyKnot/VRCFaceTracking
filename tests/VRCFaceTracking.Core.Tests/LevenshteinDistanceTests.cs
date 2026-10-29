using VRCFaceTracking.Core.Helpers;

namespace VRCFaceTracking.Core.Tests;

public sealed class LevenshteinDistanceTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("sitting", "kitten", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("VirtualDesktop", "VirtualDesktop.VRCFaceTracking", 16)]
    [InlineData("abc", "ABC", 3)]
    public void CalculateMatchesKnownDistances(string source1, string source2, int expected) =>
        Assert.Equal(expected, LevenshteinDistance.Calculate(source1, source2));
}
