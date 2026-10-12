using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Types;

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

    private static EyeAssists Limited(float maxHorizontal, float maxVertical)
    {
        var (mutator, _) = Create();
        mutator.limitEyeRange = true;
        mutator.maxHorizontal = maxHorizontal;
        mutator.maxVertical = maxVertical;
        return mutator;
    }

    private static UnifiedTrackingData Gaze(float leftX, float leftY, float rightX, float rightY)
    {
        var data = Data(1f, 1f);
        data.Eye.Left.Gaze = new Vector2(leftX, leftY);
        data.Eye.Right.Gaze = new Vector2(rightX, rightY);
        return data;
    }

    private static float ShareOfRange(float gaze) => new Vector2(gaze, 0f).ToNormalizedYaw();

    private static float GazeAtShare(double share) => (float)Math.Tan(share * Math.PI / 4);

    private const string SettingsBeforeRangeLimit =
        "{\"convergenceFix\":false,\"convergenceStrength\":0.6,\"closeAssist\":true,\"closeAssistStrength\":1.0," +
        "\"eyelidSync\":true,\"syncStrength\":1.0,\"syncToMostOpen\":false,\"preserveWinks\":false," +
        "\"forceEyesClosed\":false,\"Name\":\"Eye Assists\",\"Step\":2,\"IsActive\":true,\"IsSaved\":false}";

    [Fact]
    public void RangeLimit_OffLeavesGazeUntouched()
    {
        var (mutator, _) = Create();
        mutator.maxHorizontal = 0.1f;
        mutator.maxVertical = 0.1f;
        var data = Gaze(0.9f, -1.3f, -0.45f, 0.7f);

        mutator.MutateData(ref data);

        Assert.Equal(0.9f, data.Eye.Left.Gaze.x);
        Assert.Equal(-1.3f, data.Eye.Left.Gaze.y);
        Assert.Equal(-0.45f, data.Eye.Right.Gaze.x);
        Assert.Equal(0.7f, data.Eye.Right.Gaze.y);
    }

    [Theory]
    [InlineData(1f, -1f, -1.2f, 0.8f)]
    [InlineData(-3f, 2.5f, 0.9f, -0.95f)]
    public void RangeLimit_CapsBothEyesOnBothAxes(float leftX, float leftY, float rightX, float rightY)
    {
        var mutator = Limited(0.3f, 0.2f);
        var data = Gaze(leftX, leftY, rightX, rightY);

        mutator.MutateData(ref data);

        AssertAtCap(0.3f, leftX, data.Eye.Left.Gaze.x);
        AssertAtCap(0.2f, leftY, data.Eye.Left.Gaze.y);
        AssertAtCap(0.3f, rightX, data.Eye.Right.Gaze.x);
        AssertAtCap(0.2f, rightY, data.Eye.Right.Gaze.y);
    }

    private static void AssertAtCap(float max, float input, float output)
    {
        Assert.Equal(Math.Sign(input), Math.Sign(output));
        Assert.InRange(Math.Abs(ShareOfRange(output)), max * 0.99f, max + 1e-6f);
    }

    [Theory]
    [InlineData(0.6f, 0.5f, 0.412689f)]
    [InlineData(-0.6f, 0.5f, -0.412689f)]
    [InlineData(0.4f, 0.5f, 0.380599f)]
    [InlineData(1f, 0.3f, 0.240079f)]
    [InlineData(-1f, 0.3f, -0.240079f)]
    public void RangeLimit_FollowsTheKneeCurve(float input, float max, float expected)
    {
        var mutator = Limited(max, max);
        var data = Gaze(input, input, input, input);

        mutator.MutateData(ref data);

        Assert.Equal(expected, data.Eye.Left.Gaze.x, 5);
        Assert.Equal(expected, data.Eye.Right.Gaze.y, 5);
    }

    [Fact]
    public void RangeLimit_FullGazeLandsOnTheSliderShareOfNativeRange()
    {
        var mutator = Limited(0.3f, 0.2f);
        var data = Gaze(1f, 1f, -1f, -1f);

        mutator.MutateData(ref data);

        Assert.Equal(13.5f, data.Eye.Left.Gaze.ToYaw(), 2);
        Assert.Equal(-9f, data.Eye.Left.Gaze.ToPitch(), 2);
        Assert.Equal(-13.5f, data.Eye.Right.Gaze.ToYaw(), 2);
        Assert.Equal(9f, data.Eye.Right.Gaze.ToPitch(), 2);
        Assert.Equal(0.3f, data.Eye.Left.Gaze.ToNormalized().x, 3);
        Assert.Equal(-0.2f, data.Eye.Left.Gaze.ToNormalized().y, 3);
        Assert.InRange(data.Eye.Left.Gaze.x, 0f, 0.3f);
        Assert.InRange(data.Eye.Left.Gaze.y, 0f, 0.2f);
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.15f)]
    [InlineData(-0.2f)]
    public void RangeLimit_LeavesValuesBelowTheKneeExact(float gaze)
    {
        var mutator = Limited(0.35f, 0.35f);
        var data = Gaze(gaze, -gaze, -gaze, gaze);

        mutator.MutateData(ref data);

        Assert.Equal(gaze, data.Eye.Left.Gaze.x);
        Assert.Equal(-gaze, data.Eye.Left.Gaze.y);
        Assert.Equal(-gaze, data.Eye.Right.Gaze.x);
        Assert.Equal(gaze, data.Eye.Right.Gaze.y);
    }

    [Theory]
    [InlineData(0.8, 0.001f)]
    [InlineData(0.9, 0.02f)]
    [InlineData(1.0, 0.06f)]
    public void RangeLimit_BarelyMovesValuesInsideTheLimit(double shareOfLimit, float maxShrink)
    {
        var mutator = Limited(0.35f, 0.35f);
        var input = GazeAtShare(shareOfLimit * 0.35);
        var data = Gaze(input, input, -input, -input);

        mutator.MutateData(ref data);

        Assert.InRange(1f - ShareOfRange(data.Eye.Left.Gaze.x) / ShareOfRange(input), 0f, maxShrink);
        Assert.InRange(1f - ShareOfRange(data.Eye.Right.Gaze.y) / ShareOfRange(-input), 0f, maxShrink);
    }

    private static List<(float input, float x, float y)> SweepLimit(float maxHorizontal, float maxVertical)
    {
        var mutator = Limited(maxHorizontal, maxVertical);
        var samples = new List<(float, float, float)>();
        for (var i = -800; i <= 800; i++)
        {
            var input = i / 400f;
            var data = Gaze(input, input, input, input);
            mutator.MutateData(ref data);
            samples.Add((input, data.Eye.Left.Gaze.x, data.Eye.Right.Gaze.y));
        }
        return samples;
    }

    [Fact]
    public void RangeLimit_KneeIsMonotonic()
    {
        var samples = SweepLimit(0.35f, 0.2f);

        for (var i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].x >= samples[i - 1].x, $"x fell at input {samples[i].input}");
            Assert.True(samples[i].y >= samples[i - 1].y, $"y fell at input {samples[i].input}");
        }
        Assert.InRange(ShareOfRange(samples[^1].x), 0.3499f, 0.35f + 1e-6f);
        Assert.InRange(ShareOfRange(samples[^1].y), 0.1999f, 0.2f + 1e-6f);
    }

    [Fact]
    public void RangeLimit_KneeHasNoJumps()
    {
        var samples = SweepLimit(0.35f, 0.2f);

        for (var i = 1; i < samples.Count; i++)
        {
            var step = samples[i].input - samples[i - 1].input;
            Assert.True(samples[i].x - samples[i - 1].x <= step + 1e-6f, $"x jumped at input {samples[i].input}");
            Assert.True(samples[i].y - samples[i - 1].y <= step + 1e-6f, $"y jumped at input {samples[i].input}");
        }
    }

    [Fact]
    public void RangeLimit_MaxOfOneLeavesThatAxisUnlimited()
    {
        var mutator = Limited(1f, 0.3f);
        var data = Gaze(1.4f, 1.4f, -1.4f, -1.4f);

        mutator.MutateData(ref data);

        Assert.Equal(1.4f, data.Eye.Left.Gaze.x);
        Assert.Equal(-1.4f, data.Eye.Right.Gaze.x);
        Assert.Equal(0.240079f, data.Eye.Left.Gaze.y, 5);
        Assert.Equal(-0.240079f, data.Eye.Right.Gaze.y, 5);
    }

    [Fact]
    public void RangeLimit_AppliesToConvergedGaze()
    {
        var mutator = Limited(0.5f, 1f);
        mutator.convergenceFix = true;
        mutator.convergenceStrength = 1f;
        var data = Gaze(0.2f, 0f, 0.6f, 0f);

        mutator.MutateData(ref data);

        Assert.Equal(0.380599f, data.Eye.Left.Gaze.x, 5);
        Assert.Equal(0.380599f, data.Eye.Right.Gaze.x, 5);
    }

    [Fact]
    public void RangeLimit_ForceEyesClosedStillCentersGaze()
    {
        var mutator = Limited(0.3f, 0.3f);
        mutator.forceEyesClosed = true;
        var data = Gaze(0.9f, -0.9f, 0.9f, -0.9f);

        mutator.MutateData(ref data);

        Assert.Equal(0f, data.Eye.Left.Openness);
        Assert.Equal(0f, data.Eye.Right.Openness);
        Assert.Equal(0f, data.Eye.Left.Gaze.x);
        Assert.Equal(0f, data.Eye.Left.Gaze.y);
        Assert.Equal(0f, data.Eye.Right.Gaze.x);
        Assert.Equal(0f, data.Eye.Right.Gaze.y);
    }

    [Fact]
    public async Task RangeLimit_OldSettingsLoadWithDefaults()
    {
        var loaded = await Json.ToObjectAsync<EyeAssists>(SettingsBeforeRangeLimit);

        Assert.True(loaded.IsActive);
        Assert.True(loaded.closeAssist);
        Assert.Equal(1f, loaded.closeAssistStrength);
        Assert.False(loaded.preserveWinks);
        Assert.False(loaded.limitEyeRange);
        Assert.Equal(0.35f, loaded.maxHorizontal);
        Assert.Equal(0.35f, loaded.maxVertical);
    }

    [Fact]
    public async Task RangeLimit_OldSettingsLeaveGazeUntouched()
    {
        var loaded = await Json.ToObjectAsync<EyeAssists>(SettingsBeforeRangeLimit);
        var data = Gaze(0.9f, -1.3f, -0.45f, 0.7f);

        loaded.MutateData(ref data);

        Assert.Equal(0.9f, data.Eye.Left.Gaze.x);
        Assert.Equal(-1.3f, data.Eye.Left.Gaze.y);
        Assert.Equal(-0.45f, data.Eye.Right.Gaze.x);
        Assert.Equal(0.7f, data.Eye.Right.Gaze.y);
    }

    [Fact]
    public async Task RangeLimit_SettingsSurviveSaveAndLoad()
    {
        var mutation = new EyeAssists { limitEyeRange = true, maxHorizontal = 0.42f, maxVertical = 0.27f };

        var loaded = await Json.ToObjectAsync<EyeAssists>(await Json.StringifyAsync(mutation));

        Assert.True(loaded.limitEyeRange);
        Assert.Equal(0.42f, loaded.maxHorizontal);
        Assert.Equal(0.27f, loaded.maxVertical);
    }

    [Fact]
    public void RangeLimit_ShowsAfterConvergenceSettings()
    {
        var mutation = new EyeAssists();
        mutation.CreateProperties();
        var rows = mutation.Components.OfType<MutationProperty>().ToList();
        var start = rows.FindIndex(r => r.Name == "Limit Eye Range");

        Assert.Equal(
            new[] { "Convergence Strength", "Limit Eye Range", "Max Horizontal", "Max Vertical", "Eye Close Assist" },
            rows.Skip(start - 1).Take(5).Select(r => r.Name));
        Assert.Equal(MutationPropertyType.CheckBox, rows[start].Type);
        Assert.All(rows.Skip(start + 1).Take(2), r =>
        {
            Assert.Equal(MutationPropertyType.Slider, r.Type);
            Assert.Equal(0f, r.Min);
            Assert.Equal(1f, r.Max);
        });
    }
}
