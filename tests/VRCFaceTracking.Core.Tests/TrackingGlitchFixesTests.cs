using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class TrackingGlitchFixesTests
{
    private const double Tick = 0.025;

    private readonly UnifiedTrackingData _raw = new();
    private double _now;
    private VoiceReading? _voice;
    private int _jitter;

    private TrackingGlitchFixes Create(bool quiet = false, bool stuck = false, bool relax = false) => new()
    {
        QuietMouth = quiet,
        StuckEyelidFallback = stuck,
        RelaxOnFreeze = relax,
        NowSeconds = () => _now,
        RawInput = () => _raw,
        Voice = () => _voice,
    };

    private static float Shape(UnifiedTrackingData data, UnifiedExpressions shape) => data.Shapes[(int)shape].Weight;

    private static void SetShape(UnifiedTrackingData data, UnifiedExpressions shape, float weight) => data.Shapes[(int)shape].Weight = weight;

    private UnifiedTrackingData Step(TrackingGlitchFixes fixes)
    {
        _now += Tick;
        var data = new UnifiedTrackingData();
        data.CopyPropertiesOf(_raw);
        fixes.MutateData(ref data);
        return data;
    }

    private UnifiedTrackingData Run(TrackingGlitchFixes fixes, double seconds, Action<int>? beforeTick = null)
    {
        UnifiedTrackingData data = new();
        int ticks = (int)Math.Round(seconds / Tick);
        for (int i = 0; i < ticks; i++)
        {
            beforeTick?.Invoke(i);
            data = Step(fixes);
        }

        return data;
    }

    private void Jitter()
    {
        _jitter++;
        _raw.Eye.Left.Gaze.x = _jitter % 7 * 0.001f;
    }

    [Theory]
    [InlineData(0.05f, 0.12f, 0f)]
    [InlineData(0.12f, 0.12f, 0f)]
    [InlineData(0.36f, 0.12f, 0.36f)]
    [InlineData(0.7f, 0.12f, 0.7f)]
    [InlineData(0.2f, 0f, 0.2f)]
    public void TrimRemovesSmallValuesAndLeavesLargeOnesAlone(float weight, float trim, float expected) =>
        Assert.Equal(expected, TrackingGlitchFixes.Trim(weight, trim), 4);

    [Fact]
    public void TrimIsSmoothAndNeverDecreases()
    {
        const float TrimLevel = 0.12f;
        float previous = 0f;
        for (int i = 0; i <= 1000; i++)
        {
            float weight = i / 1000f;
            float trimmed = TrackingGlitchFixes.Trim(weight, TrimLevel);
            Assert.InRange(trimmed, previous, weight);
            Assert.True(trimmed - previous <= 0.0025f, $"step at {weight}");
            previous = trimmed;
        }
    }

    [Fact]
    public void QuietMouthDoesNothingWithoutVoiceData()
    {
        TrackingGlitchFixes fixes = Create(quiet: true);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.08f);

        UnifiedTrackingData data = Run(fixes, 2);

        Assert.Equal(0.08f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void QuietMouthTrimsTwitchesAfterSilenceAndKeepsRealExpressions()
    {
        TrackingGlitchFixes fixes = Create(quiet: true);
        _voice = new VoiceReading(HasLevel: true, Level: 0f, Muted: false);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.08f);
        SetShape(_raw, UnifiedExpressions.LipPuckerUpperLeft, 0.1f);
        SetShape(_raw, UnifiedExpressions.LipFunnelLowerRight, 0.6f);
        SetShape(_raw, UnifiedExpressions.MouthCornerPullLeft, 0.1f);

        UnifiedTrackingData early = Run(fixes, 0.3);
        UnifiedTrackingData settled = Run(fixes, 1);

        Assert.Equal(0.08f, Shape(early, UnifiedExpressions.JawOpen), 4);
        Assert.Equal(0f, Shape(settled, UnifiedExpressions.JawOpen), 4);
        Assert.Equal(0f, Shape(settled, UnifiedExpressions.LipPuckerUpperLeft), 4);
        Assert.Equal(0.6f, Shape(settled, UnifiedExpressions.LipFunnelLowerRight), 4);
        Assert.Equal(0.1f, Shape(settled, UnifiedExpressions.MouthCornerPullLeft), 4);
    }

    [Fact]
    public void QuietMouthLetsGoQuicklyWhenYouSpeak()
    {
        TrackingGlitchFixes fixes = Create(quiet: true);
        _voice = new VoiceReading(HasLevel: true, Level: 0f, Muted: false);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.08f);
        Run(fixes, 2);

        _voice = new VoiceReading(HasLevel: true, Level: 0.3f, Muted: false);
        UnifiedTrackingData data = Run(fixes, 0.075);

        Assert.Equal(0.08f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void QuietMouthStaysOffWhileMuted()
    {
        TrackingGlitchFixes fixes = Create(quiet: true);
        _voice = new VoiceReading(HasLevel: true, Level: 0f, Muted: true);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.08f);

        UnifiedTrackingData data = Run(fixes, 2);

        Assert.Equal(0.08f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void StuckEyelidFollowsTheEyeThatStillBlinks()
    {
        TrackingGlitchFixes fixes = Create(stuck: true);
        _raw.Eye.Right.Openness = 0.73f;
        SetShape(_raw, UnifiedExpressions.EyeSquintLeft, 0.4f);
        SetShape(_raw, UnifiedExpressions.EyeWideLeft, 0.2f);

        UnifiedTrackingData data = Run(fixes, 25, tick =>
        {
            _raw.Eye.Left.Openness = tick % 200 < 4 ? 0f : 0.9f + (tick % 3 * 0.01f);
        });

        Assert.Equal(data.Eye.Left.Openness, data.Eye.Right.Openness, 4);
        Assert.Equal(0.4f, Shape(data, UnifiedExpressions.EyeSquintRight), 4);
        Assert.Equal(0.2f, Shape(data, UnifiedExpressions.EyeWideRight), 4);

        _raw.Eye.Right.Openness = 0.5f;
        data = Step(fixes);

        Assert.Equal(0.5f, data.Eye.Right.Openness, 4);
        Assert.Equal(0f, Shape(data, UnifiedExpressions.EyeSquintRight), 4);
    }

    [Fact]
    public void StuckEyelidNeedsTheOtherEyeToBlink()
    {
        TrackingGlitchFixes fixes = Create(stuck: true);
        _raw.Eye.Right.Openness = 0.73f;

        UnifiedTrackingData data = Run(fixes, 25, tick => _raw.Eye.Left.Openness = 0.9f + (tick % 3 * 0.01f));

        Assert.Equal(0.73f, data.Eye.Right.Openness, 4);
    }

    [Fact]
    public void RelaxEasesAFrozenFaceToNeutralAndLeavesTheHeadAlone()
    {
        TrackingGlitchFixes fixes = Create(relax: true);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.5f);
        _raw.Eye.Left.Openness = 0.3f;
        _raw.Eye.Right.Gaze.y = 0.4f;
        _raw.Head.HeadYaw = 0.25f;

        UnifiedTrackingData held = Run(fixes, 2);
        UnifiedTrackingData halfway = Run(fixes, 0.5);
        UnifiedTrackingData relaxed = Run(fixes, 1);

        Assert.Equal(0.5f, Shape(held, UnifiedExpressions.JawOpen), 4);
        Assert.InRange(Shape(halfway, UnifiedExpressions.JawOpen), 0.2f, 0.3f);
        Assert.Equal(0f, Shape(relaxed, UnifiedExpressions.JawOpen), 4);
        Assert.Equal(1f, relaxed.Eye.Left.Openness, 4);
        Assert.Equal(0f, relaxed.Eye.Right.Gaze.y, 4);
        Assert.Equal(0.25f, relaxed.Head.HeadYaw, 4);
    }

    [Fact]
    public void RelaxBlendsBackWhenTrackingReturns()
    {
        TrackingGlitchFixes fixes = Create(relax: true);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.5f);
        Run(fixes, 4);

        Jitter();
        UnifiedTrackingData first = Step(fixes);
        UnifiedTrackingData back = Run(fixes, 0.15, _ => Jitter());

        Assert.InRange(Shape(first, UnifiedExpressions.JawOpen), 0f, 0.15f);
        Assert.Equal(0.5f, Shape(back, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void RelaxLeavesLiveTrackingAlone()
    {
        TrackingGlitchFixes fixes = Create(relax: true);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.5f);

        UnifiedTrackingData data = Run(fixes, 10, _ => Jitter());

        Assert.Equal(0.5f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void TogglesOffChangeNothing()
    {
        TrackingGlitchFixes fixes = Create();
        _voice = new VoiceReading(HasLevel: true, Level: 0f, Muted: false);
        SetShape(_raw, UnifiedExpressions.JawOpen, 0.08f);

        UnifiedTrackingData data = Run(fixes, 10);

        Assert.Equal(0.08f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void VoiceActivityReportsOnlyTheFirstLevel()
    {
        VoiceActivity.Clear();
        try
        {
            VoiceActivity.SetMuted(muted: true);
            Assert.True(VoiceActivity.SetLevel(0.2f));
            Assert.False(VoiceActivity.SetLevel(0.1f));
            Assert.Equal(new VoiceReading(HasLevel: true, Level: 0.1f, Muted: true), VoiceActivity.Latest);
        }
        finally
        {
            VoiceActivity.Clear();
        }
    }
}
