using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed partial class ExternalOscInputServiceTests
{
    [Theory]
    [InlineData("/avatar/parameters/EyeLidLeft", ExternalOscLidSide.Left, ExternalOscLidFormat.Unified)]
    [InlineData("/avatar/parameters/v2/EyeLidRight", ExternalOscLidSide.Right, ExternalOscLidFormat.Unified)]
    [InlineData("/avatar/parameters/FT/v2/EyeLid", ExternalOscLidSide.Both, ExternalOscLidFormat.Unified)]
    [InlineData("/EyeLidLeft", ExternalOscLidSide.Left, ExternalOscLidFormat.Unified)]
    [InlineData("/avatar/parameters/LeftEyeLidExpanded", ExternalOscLidSide.Left, ExternalOscLidFormat.Expanded)]
    [InlineData("/avatar/parameters/RightEyeLidExpanded", ExternalOscLidSide.Right, ExternalOscLidFormat.Expanded)]
    [InlineData("/avatar/parameters/CombinedEyeLidExpanded", ExternalOscLidSide.Both, ExternalOscLidFormat.Expanded)]
    [InlineData("/avatar/parameters/EyeLidExpanded", ExternalOscLidSide.Both, ExternalOscLidFormat.Expanded)]
    [InlineData("/avatar/parameters/LeftEyeLidExpandedSqueeze", ExternalOscLidSide.Left, ExternalOscLidFormat.ExpandedSqueeze)]
    [InlineData("/avatar/parameters/RightEyeLidExpandedSqueeze", ExternalOscLidSide.Right, ExternalOscLidFormat.ExpandedSqueeze)]
    [InlineData("/avatar/parameters/CombinedEyeLidExpandedSqueeze", ExternalOscLidSide.Both, ExternalOscLidFormat.ExpandedSqueeze)]
    [InlineData("/ft/EyeLidExpandedSqueeze", ExternalOscLidSide.Both, ExternalOscLidFormat.ExpandedSqueeze)]
    [InlineData("/avatar/parameters/EyeClosedLeft", ExternalOscLidSide.Left, ExternalOscLidFormat.Closed)]
    [InlineData("/avatar/parameters/EyeClosedRight", ExternalOscLidSide.Right, ExternalOscLidFormat.Closed)]
    [InlineData("/avatar/parameters/EyeClosed", ExternalOscLidSide.Both, ExternalOscLidFormat.Closed)]
    public void EyeLidFormatsResolveToLids(string address, ExternalOscLidSide side, ExternalOscLidFormat format)
    {
        ExternalOscTarget? target = ExternalOscParameterMap.Resolve(address);

        Assert.NotNull(target);
        Assert.Equal(new ExternalOscLid(side, format), target.Lid);
        Assert.Null(target.EyeChannel);
        Assert.Empty(target.Shapes);
    }

    [Theory]
    [InlineData(ExternalOscLidFormat.Unified, 0f, 0f, 0f)]
    [InlineData(ExternalOscLidFormat.Unified, 0.6f, 0.8f, 0f)]
    [InlineData(ExternalOscLidFormat.Unified, 0.75f, 1f, 0f)]
    [InlineData(ExternalOscLidFormat.Unified, 0.875f, 1f, 0.5f)]
    [InlineData(ExternalOscLidFormat.Unified, 1f, 1f, 1f)]
    [InlineData(ExternalOscLidFormat.Unified, -0.4f, 0f, 0f)]
    [InlineData(ExternalOscLidFormat.Unified, 3f, 1f, 1f)]
    [InlineData(ExternalOscLidFormat.Expanded, 0.4f, 0.5f, 0f)]
    [InlineData(ExternalOscLidFormat.Expanded, 0.8f, 1f, 0f)]
    [InlineData(ExternalOscLidFormat.Expanded, 0.9f, 1f, 0.5f)]
    [InlineData(ExternalOscLidFormat.Expanded, 1f, 1f, 1f)]
    [InlineData(ExternalOscLidFormat.Expanded, -0.5f, 0f, 0f)]
    public void OpenAndWideFormatsSplitAtTheirWidenThreshold(ExternalOscLidFormat format, float value, float openness, float widen)
    {
        ExternalOscLidValue decoded = new ExternalOscLid(ExternalOscLidSide.Left, format).Decode(value);

        Assert.Equal(openness, decoded.Openness, 4);
        Assert.NotNull(decoded.Widen);
        Assert.Equal(widen, decoded.Widen.Value, 4);
        Assert.Null(decoded.Squint);
    }

    [Theory]
    [InlineData(-0.6f, 0f, 0f, 0.6f)]
    [InlineData(-1f, 0f, 0f, 1f)]
    [InlineData(-4f, 0f, 0f, 1f)]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(0.4f, 0.5f, 0f, 0f)]
    [InlineData(0.8f, 1f, 0f, 0f)]
    [InlineData(0.9f, 1f, 0.5f, 0f)]
    [InlineData(5f, 1f, 1f, 0f)]
    public void ExpandedSqueezeTurnsNegativeValuesIntoSquint(float value, float openness, float widen, float squint)
    {
        ExternalOscLidValue decoded = new ExternalOscLid(ExternalOscLidSide.Both, ExternalOscLidFormat.ExpandedSqueeze).Decode(value);

        Assert.Equal(openness, decoded.Openness, 4);
        Assert.Equal(widen, decoded.Widen!.Value, 4);
        Assert.Equal(squint, decoded.Squint!.Value, 4);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.3f, 0.7f)]
    [InlineData(1f, 0f)]
    [InlineData(1.5f, 0f)]
    [InlineData(-1f, 1f)]
    public void ClosedValuesAreInverted(float value, float openness)
    {
        ExternalOscLidValue decoded = new ExternalOscLid(ExternalOscLidSide.Right, ExternalOscLidFormat.Closed).Decode(value);

        Assert.Equal(openness, decoded.Openness, 4);
        Assert.Null(decoded.Widen);
        Assert.Null(decoded.Squint);
    }

    [Fact]
    public void EyeLidPacketsStoreDecodedChannels()
    {
        using ExternalOscInputService service = Create();

        Assert.True(Ingest(service, Message("/avatar/parameters/v2/EyeLidLeft", 0.875f)));
        Assert.True(Ingest(service, Message("/avatar/parameters/RightEyeLidExpandedSqueeze", -0.25f)));
        Assert.True(Ingest(service, Message("/EyeClosed", 0.2f)));

        IReadOnlyDictionary<ExternalOscEyeChannel, float> eye = service.EyeSnapshot();
        Assert.Equal(1f, eye[ExternalOscEyeChannel.LeftOpennessDerived], 4);
        Assert.Equal(0.5f, eye[ExternalOscEyeChannel.LeftWiden], 4);
        Assert.False(eye.ContainsKey(ExternalOscEyeChannel.LeftSquint));
        Assert.Equal(0f, eye[ExternalOscEyeChannel.RightOpennessDerived], 4);
        Assert.Equal(0f, eye[ExternalOscEyeChannel.RightWiden], 4);
        Assert.Equal(0.25f, eye[ExternalOscEyeChannel.RightSquint], 4);
        Assert.Equal(0.8f, eye[ExternalOscEyeChannel.CombinedOpenness], 4);
        Assert.False(eye.ContainsKey(ExternalOscEyeChannel.LeftOpenness));
    }
}
