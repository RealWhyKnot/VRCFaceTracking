using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public class EyeAssistsTests
{
    private sealed class FakeClock
    {
        public double Now;
    }

    private static (EyeAssists mutator, FakeClock clock) Create()
    {
        var clock = new FakeClock();
        var mutator = new EyeAssists();
        mutator.NowSeconds = () => clock.Now;
        return (mutator, clock);
    }

    private static UnifiedTrackingData Data(float left, float right)
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = left;
        data.Eye.Right.Openness = right;
        return data;
    }

    [Theory]
    [InlineData(0.6f, 0.3f, 0f)]
    [InlineData(0.6f, 1f, 1f)]
    [InlineData(0.6f, 0.65f, 0.5f)]
    [InlineData(0f, 0.42f, 0.42f)]
    public void CloseAssist_RemapsOpenness(float strength, float input, float expected)
    {
        var (mutator, _) = Create();
        mutator.closeAssist = true;
        mutator.closeAssistStrength = strength;
        var data = Data(input, input);

        mutator.MutateData(ref data);

        Assert.Equal(expected, data.Eye.Left.Openness, 4);
        Assert.Equal(expected, data.Eye.Right.Openness, 4);
    }

    [Theory]
    [InlineData(false, 0.2f, 0.38f)]
    [InlineData(true, 0.62f, 0.8f)]
    public void EyelidSync_FirstFrameSnapsToBlendedTarget(bool mostOpen, float expectedLeft, float expectedRight)
    {
        var (mutator, _) = Create();
        mutator.eyelidSync = true;
        mutator.syncStrength = 0.7f;
        mutator.syncToMostOpen = mostOpen;
        mutator.preserveWinks = false;
        var data = Data(0.2f, 0.8f);

        mutator.MutateData(ref data);

        Assert.Equal(expectedLeft, data.Eye.Left.Openness, 4);
        Assert.Equal(expectedRight, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyelidSync_SingleEmaStepUsesClosingTau()
    {
        var (mutator, clock) = Create();
        mutator.eyelidSync = true;
        mutator.syncStrength = 0.7f;
        mutator.preserveWinks = false;
        var data = Data(0.8f, 0.8f);
        mutator.MutateData(ref data);

        clock.Now += 0.01;
        data = Data(0.2f, 0.8f);
        mutator.MutateData(ref data);

        var alpha = (float)(1.0 - Math.Exp(-0.01 / 0.018));
        Assert.Equal(0.8f + alpha * (0.2f - 0.8f), data.Eye.Left.Openness, 4);
        Assert.Equal(0.8f + alpha * (0.38f - 0.8f), data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyelidSync_ClosesFasterThanItOpens()
    {
        var (closing, closingClock) = Create();
        closing.eyelidSync = true;
        closing.syncStrength = 1f;
        closing.preserveWinks = false;
        var data = Data(1f, 1f);
        closing.MutateData(ref data);
        closingClock.Now += 0.01;
        data = Data(0.6f, 0.6f);
        closing.MutateData(ref data);
        var closingDelta = 1f - data.Eye.Left.Openness;

        var (opening, openingClock) = Create();
        opening.eyelidSync = true;
        opening.syncStrength = 1f;
        opening.preserveWinks = false;
        data = Data(0.2f, 0.2f);
        opening.MutateData(ref data);
        openingClock.Now += 0.01;
        data = Data(0.6f, 0.6f);
        opening.MutateData(ref data);
        var openingDelta = data.Eye.Left.Openness - 0.2f;

        Assert.True(closingDelta > openingDelta);
    }

    [Fact]
    public void EyelidSync_SustainedWinkBypassesSync()
    {
        var (mutator, clock) = Create();
        mutator.eyelidSync = true;
        mutator.syncStrength = 1f;
        mutator.preserveWinks = true;
        var data = Data(1f, 1f);
        mutator.MutateData(ref data);

        clock.Now = 0.01;
        data = Data(1f, 0.2f);
        mutator.MutateData(ref data);
        Assert.True(data.Eye.Left.Openness < 1f);

        clock.Now = 0.12;
        data = Data(1f, 0.2f);
        mutator.MutateData(ref data);
        Assert.True(data.Eye.Left.Openness < 1f);

        clock.Now = 0.14;
        data = Data(1f, 0.2f);
        mutator.MutateData(ref data);
        Assert.Equal(1f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.2f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyelidSync_WinkDwellResetsWhenAsymmetryDrops()
    {
        var (mutator, clock) = Create();
        mutator.eyelidSync = true;
        mutator.syncStrength = 1f;
        mutator.preserveWinks = true;
        var data = Data(1f, 0.2f);
        mutator.MutateData(ref data);

        clock.Now = 0.2;
        data = Data(0.5f, 0.5f);
        mutator.MutateData(ref data);

        clock.Now = 0.21;
        data = Data(1f, 0.2f);
        mutator.MutateData(ref data);
        Assert.True(data.Eye.Left.Openness < 1f);
    }

    [Fact]
    public void EyelidSync_SnapsAfterLongGap()
    {
        var (mutator, clock) = Create();
        mutator.eyelidSync = true;
        mutator.syncStrength = 1f;
        mutator.preserveWinks = false;
        var data = Data(1f, 1f);
        mutator.MutateData(ref data);

        clock.Now = 0.6;
        data = Data(0.3f, 0.3f);
        mutator.MutateData(ref data);

        Assert.Equal(0.3f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.3f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyelidSync_BlendsEyeWideWithoutEma()
    {
        var (mutator, _) = Create();
        mutator.eyelidSync = true;
        mutator.syncStrength = 0.5f;
        mutator.preserveWinks = false;
        var data = Data(0.5f, 0.5f);
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = 0.8f;
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = 0.2f;

        mutator.MutateData(ref data);

        Assert.Equal(0.5f, data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight, 4);
        Assert.Equal(0.2f, data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight, 4);
    }

    [Theory]
    [InlineData(1f, -0.2f, 0.2f, 0f, 0f)]
    [InlineData(0.5f, -0.2f, 0.2f, -0.1f, 0.1f)]
    [InlineData(1f, 0.1f, 0.5f, 0.3f, 0.3f)]
    [InlineData(1f, 0.2f, -0.2f, 0.2f, -0.2f)]
    public void Convergence_ClampsDivergenceOnly(float strength, float leftX, float rightX, float expectedLeft, float expectedRight)
    {
        var (mutator, _) = Create();
        mutator.convergenceFix = true;
        mutator.convergenceStrength = strength;
        var data = Data(1f, 1f);
        data.Eye.Left.Gaze.x = leftX;
        data.Eye.Right.Gaze.x = rightX;

        mutator.MutateData(ref data);

        Assert.Equal(expectedLeft, data.Eye.Left.Gaze.x, 4);
        Assert.Equal(expectedRight, data.Eye.Right.Gaze.x, 4);
    }

    [Fact]
    public void ForceEyesClosed_OverridesEverything()
    {
        var (mutator, _) = Create();
        mutator.eyelidSync = true;
        mutator.closeAssist = true;
        mutator.forceEyesClosed = true;
        var data = Data(1f, 0.8f);
        data.Eye.Left.Gaze.x = 0.4f;
        data.Eye.Right.Gaze.y = -0.3f;
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = 0.7f;
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = 0.7f;

        mutator.MutateData(ref data);

        Assert.Equal(0f, data.Eye.Left.Openness);
        Assert.Equal(0f, data.Eye.Right.Openness);
        Assert.Equal(0f, data.Eye.Left.Gaze.x);
        Assert.Equal(0f, data.Eye.Right.Gaze.y);
        Assert.Equal(0f, data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight);
        Assert.Equal(0f, data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight);
    }
}
