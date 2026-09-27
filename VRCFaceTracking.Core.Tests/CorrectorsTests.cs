using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public class CorrectorsTests
{
    private static UnifiedTrackingData Blend(float blend, float left, float right)
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = left;
        data.Eye.Right.Openness = right;
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = left;
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = right;
        new Correctors { eyeLidBlend = blend }.MutateData(ref data);
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
}
