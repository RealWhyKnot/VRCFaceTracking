using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public class EyelidBlendTests
{
    private static UnifiedTrackingData Blend(float blend, float left, float right)
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = left;
        data.Eye.Right.Openness = right;
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = left;
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = right;
        new EyeAssists { eyeLidBlend = blend }.MutateData(ref data);
        return data;
    }

    [Fact]
    public void FullBlend_GivesBothEyesTheAverage()
    {
        var data = Blend(1f, 0.2f, 0.8f);

        Assert.Equal(0.5f, data.Eye.Left.Openness, 5);
        Assert.Equal(0.5f, data.Eye.Right.Openness, 5);
        Assert.Equal(0.5f, data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight, 5);
        Assert.Equal(0.5f, data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight, 5);
    }

    [Fact]
    public void HalfBlend_MixesEachEyeWithTheOthersOriginalValue()
    {
        var data = Blend(0.5f, 0.2f, 0.8f);

        Assert.Equal(0.35f, data.Eye.Left.Openness, 5);
        Assert.Equal(0.65f, data.Eye.Right.Openness, 5);
    }

    [Fact]
    public void NoBlend_LeavesEyesAlone()
    {
        var data = Blend(0f, 0.2f, 0.8f);

        Assert.Equal(0.2f, data.Eye.Left.Openness);
        Assert.Equal(0.8f, data.Eye.Right.Openness);
    }

    [Fact]
    public void FullBlend_AveragesPupilsInMillimetres()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.PupilDiameter_MM = 3f;
        data.Eye.Right.PupilDiameter_MM = 5f;

        new EyeAssists { eyeLidBlend = 1f }.MutateData(ref data);

        Assert.Equal(4f, data.Eye.Left.PupilDiameter_MM, 5);
        Assert.Equal(4f, data.Eye.Right.PupilDiameter_MM, 5);
    }

    [Fact]
    public void Correctors_NoLongerTouchEyelids()
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0f;
        data.Eye.Right.Openness = 1f;

        new Correctors().MutateData(ref data);

        Assert.Equal(0f, data.Eye.Left.Openness);
        Assert.Equal(1f, data.Eye.Right.Openness);
    }

    [Fact]
    public async Task OldCorrectorsSave_WithEyelidBlend_StillLoads()
    {
        const string saved = "{\"mouthClosedFix\":false,\"lipSuckFix\":true,\"eyeLidBlend\":1.0,\"eyeLookSymmetrize\":false,\"Name\":\"Unified Correctors\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

        var loaded = await Json.ToObjectAsync<Correctors>(saved);

        Assert.False(loaded.mouthClosedFix);
        Assert.True(loaded.IsActive);
    }
}
