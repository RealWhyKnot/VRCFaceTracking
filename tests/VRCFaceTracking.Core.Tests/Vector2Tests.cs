using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

public class Vector2Tests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(-1f, -1f)]
    [InlineData(0.41421356f, 0.5f)]
    public void ToNormalizedYaw_MapsFortyFiveDegreesToOne(float x, float expected)
    {
        var gaze = new Vector2(x, 0f);
        Assert.Equal(expected, gaze.ToNormalizedYaw(), 4);
    }

    [Theory]
    [InlineData(1f, -1f)]
    [InlineData(-0.41421356f, 0.5f)]
    public void ToNormalizedPitch_IsInvertedLikeToPitch(float y, float expected)
    {
        var gaze = new Vector2(0f, y);
        Assert.Equal(expected, gaze.ToNormalizedPitch(), 4);
        Assert.Equal(gaze.ToPitch() / 45f, gaze.ToNormalizedPitch(), 5);
    }

    [Fact]
    public void ToNormalized_CombinesBothAxes()
    {
        var gaze = new Vector2(1f, -1f).ToNormalized();
        Assert.Equal(1f, gaze.x, 4);
        Assert.Equal(1f, gaze.y, 4);
    }
}
