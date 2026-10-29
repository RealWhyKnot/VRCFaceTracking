using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

public sealed class EyeAssistsTests
{
    private sealed class FakeClock
    {
        public double Now;
    }

    private static (EyeAssists mutator, FakeClock clock) Create()
    {
        var clock = new FakeClock();
        var mutator = new EyeAssists
        {
            NowSeconds = () => clock.Now,
        };
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
    public void CloseAssistRemapsOpenness(float strength, float input, float expected)
    {
        (EyeAssists? mutator, FakeClock _) = Create();
        mutator.CloseAssist = true;
        mutator.CloseAssistStrength = strength;
        UnifiedTrackingData data = Data(input, input);

        mutator.MutateData(ref data);

        Assert.Equal(expected, data.Eye.Left.Openness, 4);
        Assert.Equal(expected, data.Eye.Right.Openness, 4);
    }

    [Theory]
    [InlineData(false, 0.2f, 0.38f)]
    [InlineData(true, 0.62f, 0.8f)]
    public void EyelidSyncFirstFrameSnapsToBlendedTarget(bool mostOpen, float expectedLeft, float expectedRight)
    {
        (EyeAssists? mutator, FakeClock _) = Create();
        mutator.EyelidSync = true;
        mutator.SyncStrength = 0.7f;
        mutator.SyncToMostOpen = mostOpen;
        mutator.PreserveWinks = false;
        UnifiedTrackingData data = Data(0.2f, 0.8f);

        mutator.MutateData(ref data);

        Assert.Equal(expectedLeft, data.Eye.Left.Openness, 4);
        Assert.Equal(expectedRight, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyelidSyncSingleEmaStepUsesClosingTau()
    {
        (EyeAssists? mutator, FakeClock? clock) = Create();
        mutator.EyelidSync = true;
        mutator.SyncStrength = 0.7f;
        mutator.PreserveWinks = false;
        UnifiedTrackingData data = Data(0.8f, 0.8f);
        mutator.MutateData(ref data);

        clock.Now += 0.01;
        data = Data(0.2f, 0.8f);
        mutator.MutateData(ref data);

        float alpha = (float)(1.0 - Math.Exp(-0.01 / 0.018));
        Assert.Equal(0.8f + (alpha * (0.2f - 0.8f)), data.Eye.Left.Openness, 4);
        Assert.Equal(0.8f + (alpha * (0.38f - 0.8f)), data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyelidSyncClosesFasterThanItOpens()
    {
        (EyeAssists? closing, FakeClock? closingClock) = Create();
        closing.EyelidSync = true;
        closing.SyncStrength = 1f;
        closing.PreserveWinks = false;
        UnifiedTrackingData data = Data(1f, 1f);
        closing.MutateData(ref data);
        closingClock.Now += 0.01;
        data = Data(0.6f, 0.6f);
        closing.MutateData(ref data);
        float closingDelta = 1f - data.Eye.Left.Openness;

        (EyeAssists? opening, FakeClock? openingClock) = Create();
        opening.EyelidSync = true;
        opening.SyncStrength = 1f;
        opening.PreserveWinks = false;
        data = Data(0.2f, 0.2f);
        opening.MutateData(ref data);
        openingClock.Now += 0.01;
        data = Data(0.6f, 0.6f);
        opening.MutateData(ref data);
        float openingDelta = data.Eye.Left.Openness - 0.2f;

        Assert.True(closingDelta > openingDelta);
    }

    [Fact]
    public void EyelidSyncSustainedWinkBypassesSync()
    {
        (EyeAssists? mutator, FakeClock? clock) = Create();
        mutator.EyelidSync = true;
        mutator.SyncStrength = 1f;
        mutator.PreserveWinks = true;
        UnifiedTrackingData data = Data(1f, 1f);
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
    public void EyelidSyncWinkDwellResetsWhenAsymmetryDrops()
    {
        (EyeAssists? mutator, FakeClock? clock) = Create();
        mutator.EyelidSync = true;
        mutator.SyncStrength = 1f;
        mutator.PreserveWinks = true;
        UnifiedTrackingData data = Data(1f, 0.2f);
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
    public void EyelidSyncSnapsAfterLongGap()
    {
        (EyeAssists? mutator, FakeClock? clock) = Create();
        mutator.EyelidSync = true;
        mutator.SyncStrength = 1f;
        mutator.PreserveWinks = false;
        UnifiedTrackingData data = Data(1f, 1f);
        mutator.MutateData(ref data);

        clock.Now = 0.6;
        data = Data(0.3f, 0.3f);
        mutator.MutateData(ref data);

        Assert.Equal(0.3f, data.Eye.Left.Openness, 4);
        Assert.Equal(0.3f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void EyelidSyncBlendsEyeWideWithoutEma()
    {
        (EyeAssists? mutator, FakeClock _) = Create();
        mutator.EyelidSync = true;
        mutator.SyncStrength = 0.5f;
        mutator.PreserveWinks = false;
        UnifiedTrackingData data = Data(0.5f, 0.5f);
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
    public void ConvergenceClampsDivergenceOnly(float strength, float leftX, float rightX, float expectedLeft, float expectedRight)
    {
        (EyeAssists? mutator, FakeClock _) = Create();
        mutator.ConvergenceFix = true;
        mutator.ConvergenceStrength = strength;
        UnifiedTrackingData data = Data(1f, 1f);
        data.Eye.Left.Gaze.x = leftX;
        data.Eye.Right.Gaze.x = rightX;

        mutator.MutateData(ref data);

        Assert.Equal(expectedLeft, data.Eye.Left.Gaze.x, 4);
        Assert.Equal(expectedRight, data.Eye.Right.Gaze.x, 4);
    }

    [Fact]
    public void ForceEyesClosedOverridesEverything()
    {
        (EyeAssists? mutator, FakeClock _) = Create();
        mutator.EyelidSync = true;
        mutator.CloseAssist = true;
        mutator.ForceEyesClosed = true;
        UnifiedTrackingData data = Data(1f, 0.8f);
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
        (EyeAssists? mutator, FakeClock _) = Create();
        mutator.LimitEyeRange = true;
        mutator.MaxHorizontal = maxHorizontal;
        mutator.MaxVertical = maxVertical;
        return mutator;
    }

    private static UnifiedTrackingData Gaze(float leftX, float leftY, float rightX, float rightY)
    {
        UnifiedTrackingData data = Data(1f, 1f);
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
    public void RangeLimitOffLeavesGazeUntouched()
    {
        (EyeAssists? mutator, FakeClock _) = Create();
        mutator.MaxHorizontal = 0.1f;
        mutator.MaxVertical = 0.1f;
        UnifiedTrackingData data = Gaze(0.9f, -1.3f, -0.45f, 0.7f);

        mutator.MutateData(ref data);

        Assert.Equal(0.9f, data.Eye.Left.Gaze.x);
        Assert.Equal(-1.3f, data.Eye.Left.Gaze.y);
        Assert.Equal(-0.45f, data.Eye.Right.Gaze.x);
        Assert.Equal(0.7f, data.Eye.Right.Gaze.y);
    }

    [Theory]
    [InlineData(1f, -1f, -1.2f, 0.8f)]
    [InlineData(-3f, 2.5f, 0.9f, -0.95f)]
    public void RangeLimitCapsBothEyesOnBothAxes(float leftX, float leftY, float rightX, float rightY)
    {
        EyeAssists mutator = Limited(0.3f, 0.2f);
        UnifiedTrackingData data = Gaze(leftX, leftY, rightX, rightY);

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
    public void RangeLimitFollowsTheKneeCurve(float input, float max, float expected)
    {
        EyeAssists mutator = Limited(max, max);
        UnifiedTrackingData data = Gaze(input, input, input, input);

        mutator.MutateData(ref data);

        Assert.Equal(expected, data.Eye.Left.Gaze.x, 5);
        Assert.Equal(expected, data.Eye.Right.Gaze.y, 5);
    }

    [Fact]
    public void RangeLimitFullGazeLandsOnTheSliderShareOfNativeRange()
    {
        EyeAssists mutator = Limited(0.3f, 0.2f);
        UnifiedTrackingData data = Gaze(1f, 1f, -1f, -1f);

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
    public void RangeLimitLeavesValuesBelowTheKneeExact(float gaze)
    {
        EyeAssists mutator = Limited(0.35f, 0.35f);
        UnifiedTrackingData data = Gaze(gaze, -gaze, -gaze, gaze);

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
    public void RangeLimitBarelyMovesValuesInsideTheLimit(double shareOfLimit, float maxShrink)
    {
        EyeAssists mutator = Limited(0.35f, 0.35f);
        float input = GazeAtShare(shareOfLimit * 0.35);
        UnifiedTrackingData data = Gaze(input, input, -input, -input);

        mutator.MutateData(ref data);

        Assert.InRange(1f - (ShareOfRange(data.Eye.Left.Gaze.x) / ShareOfRange(input)), 0f, maxShrink);
        Assert.InRange(1f - (ShareOfRange(data.Eye.Right.Gaze.y) / ShareOfRange(-input)), 0f, maxShrink);
    }

    private static List<(float input, float x, float y)> SweepLimit(float maxHorizontal, float maxVertical)
    {
        EyeAssists mutator = Limited(maxHorizontal, maxVertical);
        var samples = new List<(float, float, float)>();
        for (int i = -800; i <= 800; i++)
        {
            float input = i / 400f;
            UnifiedTrackingData data = Gaze(input, input, input, input);
            mutator.MutateData(ref data);
            samples.Add((input, data.Eye.Left.Gaze.x, data.Eye.Right.Gaze.y));
        }
        return samples;
    }

    [Fact]
    public void RangeLimitKneeIsMonotonic()
    {
        List<(float input, float x, float y)> samples = SweepLimit(0.35f, 0.2f);

        for (int i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].x >= samples[i - 1].x, $"x fell at input {samples[i].input}");
            Assert.True(samples[i].y >= samples[i - 1].y, $"y fell at input {samples[i].input}");
        }
        Assert.InRange(ShareOfRange(samples[^1].x), 0.3499f, 0.35f + 1e-6f);
        Assert.InRange(ShareOfRange(samples[^1].y), 0.1999f, 0.2f + 1e-6f);
    }

    [Fact]
    public void RangeLimitKneeHasNoJumps()
    {
        List<(float input, float x, float y)> samples = SweepLimit(0.35f, 0.2f);

        for (int i = 1; i < samples.Count; i++)
        {
            float step = samples[i].input - samples[i - 1].input;
            Assert.True(samples[i].x - samples[i - 1].x <= step + 1e-6f, $"x jumped at input {samples[i].input}");
            Assert.True(samples[i].y - samples[i - 1].y <= step + 1e-6f, $"y jumped at input {samples[i].input}");
        }
    }

    [Fact]
    public void RangeLimitMaxOfOneLeavesThatAxisUnlimited()
    {
        EyeAssists mutator = Limited(1f, 0.3f);
        UnifiedTrackingData data = Gaze(1.4f, 1.4f, -1.4f, -1.4f);

        mutator.MutateData(ref data);

        Assert.Equal(1.4f, data.Eye.Left.Gaze.x);
        Assert.Equal(-1.4f, data.Eye.Right.Gaze.x);
        Assert.Equal(0.240079f, data.Eye.Left.Gaze.y, 5);
        Assert.Equal(-0.240079f, data.Eye.Right.Gaze.y, 5);
    }

    [Fact]
    public void RangeLimitAppliesToConvergedGaze()
    {
        EyeAssists mutator = Limited(0.5f, 1f);
        mutator.ConvergenceFix = true;
        mutator.ConvergenceStrength = 1f;
        UnifiedTrackingData data = Gaze(0.2f, 0f, 0.6f, 0f);

        mutator.MutateData(ref data);

        Assert.Equal(0.380599f, data.Eye.Left.Gaze.x, 5);
        Assert.Equal(0.380599f, data.Eye.Right.Gaze.x, 5);
    }

    [Fact]
    public void RangeLimitForceEyesClosedStillCentersGaze()
    {
        EyeAssists mutator = Limited(0.3f, 0.3f);
        mutator.ForceEyesClosed = true;
        UnifiedTrackingData data = Gaze(0.9f, -0.9f, 0.9f, -0.9f);

        mutator.MutateData(ref data);

        Assert.Equal(0f, data.Eye.Left.Openness);
        Assert.Equal(0f, data.Eye.Right.Openness);
        Assert.Equal(0f, data.Eye.Left.Gaze.x);
        Assert.Equal(0f, data.Eye.Left.Gaze.y);
        Assert.Equal(0f, data.Eye.Right.Gaze.x);
        Assert.Equal(0f, data.Eye.Right.Gaze.y);
    }

    [Fact]
    public async Task RangeLimitOldSettingsLoadWithDefaults()
    {
        EyeAssists loaded = (await JsonText.ToObjectAsync<EyeAssists>(SettingsBeforeRangeLimit, CancellationToken.None))!;

        Assert.True(loaded.IsActive);
        Assert.True(loaded.CloseAssist);
        Assert.Equal(1f, loaded.CloseAssistStrength);
        Assert.False(loaded.PreserveWinks);
        Assert.False(loaded.LimitEyeRange);
        Assert.Equal(0.35f, loaded.MaxHorizontal);
        Assert.Equal(0.35f, loaded.MaxVertical);
    }

    [Fact]
    public async Task RangeLimitOldSettingsLeaveGazeUntouched()
    {
        EyeAssists loaded = (await JsonText.ToObjectAsync<EyeAssists>(SettingsBeforeRangeLimit, CancellationToken.None))!;
        UnifiedTrackingData data = Gaze(0.9f, -1.3f, -0.45f, 0.7f);

        loaded.MutateData(ref data);

        Assert.Equal(0.9f, data.Eye.Left.Gaze.x);
        Assert.Equal(-1.3f, data.Eye.Left.Gaze.y);
        Assert.Equal(-0.45f, data.Eye.Right.Gaze.x);
        Assert.Equal(0.7f, data.Eye.Right.Gaze.y);
    }

    [Fact]
    public async Task RangeLimitSettingsSurviveSaveAndLoad()
    {
        var mutation = new EyeAssists { LimitEyeRange = true, MaxHorizontal = 0.42f, MaxVertical = 0.27f };

        EyeAssists loaded = (await JsonText.ToObjectAsync<EyeAssists>(await JsonText.StringifyAsync(mutation, CancellationToken.None), CancellationToken.None))!;

        Assert.True(loaded.LimitEyeRange);
        Assert.Equal(0.42f, loaded.MaxHorizontal);
        Assert.Equal(0.27f, loaded.MaxVertical);
    }

    [Fact]
    public void RangeLimitShowsAfterConvergenceSettings()
    {
        var mutation = new EyeAssists();
        mutation.CreateProperties();
        var rows = mutation.Components.OfType<MutationProperty>().ToList();
        int start = rows.FindIndex(r => string.Equals(r.Name, "Limit Eye Range", StringComparison.Ordinal));

        Assert.Equal(
            ["Convergence Strength", "Limit Eye Range", "Max Horizontal", "Max Vertical", "Eye Close Assist"],
            rows.Skip(start - 1).Take(5).Select(r => r.Name), StringComparer.Ordinal);
        Assert.Equal(MutationPropertyType.CheckBox, rows[start].Type);
        Assert.All(rows.Skip(start + 1).Take(2), r =>
        {
            Assert.Equal(MutationPropertyType.Slider, r.Type);
            Assert.Equal(0f, r.Min);
            Assert.Equal(1f, r.Max);
        });
    }
}
