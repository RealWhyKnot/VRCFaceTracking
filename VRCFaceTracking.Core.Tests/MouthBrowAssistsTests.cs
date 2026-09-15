using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public class MouthBrowAssistsTests
{
    private sealed class FakeClock
    {
        public double Now;
    }

    private static (MouthBrowAssists mutator, FakeClock clock) Create()
    {
        var clock = new FakeClock();
        var mutator = new MouthBrowAssists();
        mutator.NowSeconds = () => clock.Now;
        return (mutator, clock);
    }

    private static float Shape(UnifiedTrackingData data, UnifiedExpressions shape) => data.Shapes[(int)shape].Weight;

    private static void SetShape(UnifiedTrackingData data, UnifiedExpressions shape, float weight) => data.Shapes[(int)shape].Weight = weight;

    [Theory]
    [InlineData(true, 0.5f, 0.5f, 0.2f)]
    [InlineData(true, 0.2f, 0.5f, 0f)]
    [InlineData(false, 0.5f, 0.5f, 0.5f)]
    public void MouthCloseCompensation_ReducesJaw(bool enabled, float jaw, float mouthClosed, float expected)
    {
        var (mutator, _) = Create();
        mutator.mouthCloseCompensation = enabled;
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, jaw);
        SetShape(data, UnifiedExpressions.MouthClosed, mouthClosed);

        mutator.MutateData(ref data);

        Assert.Equal(expected, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Theory]
    [InlineData(0.35f, 0f, 0f)]
    [InlineData(1f, 0f, 0.18f)]
    [InlineData(1f, 0.5f, 0.5f)]
    [InlineData(0.675f, 0f, 0.09f)]
    public void SmileOpenAssist_FloorsJawWithSmile(float smile, float jaw, float expected)
    {
        var (mutator, _) = Create();
        mutator.smileOpenAssist = true;
        mutator.smileAssistStrength = 1f;
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.MouthCornerPullLeft, smile);
        SetShape(data, UnifiedExpressions.JawOpen, jaw);

        mutator.MutateData(ref data);

        Assert.Equal(expected, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    private static UnifiedTrackingData IdleCandidateData(float jaw = 0.15f, float mouthClosed = 0f, float activity = 0f)
    {
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, jaw);
        SetShape(data, UnifiedExpressions.MouthClosed, mouthClosed);
        SetShape(data, UnifiedExpressions.MouthCornerPullLeft, activity);
        return data;
    }

    [Fact]
    public void IdleMouthAutoClose_ClosesAfterDwell()
    {
        var (mutator, clock) = Create();
        mutator.idleMouthAutoClose = true;
        mutator.idleCloseStrength = 0.5f;

        for (var i = 0; i <= 11; i++)
        {
            clock.Now = i * 0.1;
            var frame = IdleCandidateData();
            mutator.MutateData(ref frame);
            Assert.Equal(0.15f, Shape(frame, UnifiedExpressions.JawOpen), 4);
        }

        clock.Now = 1.21;
        var data = IdleCandidateData();
        mutator.MutateData(ref data);
        Assert.Equal(0.075f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Theory]
    [InlineData(0.30f, 0f, 0f)]
    [InlineData(0.15f, 0.25f, 0f)]
    [InlineData(0.15f, 0f, 0.25f)]
    public void IdleMouthAutoClose_IgnoresNonCandidateFrames(float jaw, float mouthClosed, float activity)
    {
        var (mutator, clock) = Create();
        mutator.idleMouthAutoClose = true;
        mutator.idleCloseStrength = 0.5f;

        for (var i = 0; i <= 14; i++)
        {
            clock.Now = i * 0.1;
            var frame = IdleCandidateData(jaw, mouthClosed, activity);
            mutator.MutateData(ref frame);
            Assert.Equal(jaw, Shape(frame, UnifiedExpressions.JawOpen), 4);
        }
    }

    [Fact]
    public void IdleMouthAutoClose_NonCandidateFrameResetsDwell()
    {
        var (mutator, clock) = Create();
        mutator.idleMouthAutoClose = true;
        mutator.idleCloseStrength = 0.5f;

        for (var i = 0; i <= 6; i++)
        {
            clock.Now = i * 0.1;
            var frame = IdleCandidateData();
            mutator.MutateData(ref frame);
        }

        clock.Now = 0.7;
        var busy = IdleCandidateData(activity: 0.5f);
        mutator.MutateData(ref busy);

        for (var i = 8; i <= 19; i++)
        {
            clock.Now = i * 0.1;
            var frame = IdleCandidateData();
            mutator.MutateData(ref frame);
            Assert.Equal(0.15f, Shape(frame, UnifiedExpressions.JawOpen), 4);
        }

        clock.Now = 2.01;
        var data = IdleCandidateData();
        mutator.MutateData(ref data);
        Assert.Equal(0.075f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void BrowSync_ClosedEyeRelaxesBrows()
    {
        var (mutator, _) = Create();
        mutator.browSync = true;
        mutator.browSyncStrength = 1f;
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0f;
        data.Eye.Right.Openness = 0f;
        SetShape(data, UnifiedExpressions.BrowInnerUpLeft, 0.5f);
        SetShape(data, UnifiedExpressions.BrowOuterUpLeft, 0.5f);

        mutator.MutateData(ref data);

        Assert.Equal(0.3f, Shape(data, UnifiedExpressions.BrowInnerUpLeft), 4);
        Assert.Equal(0.3f, Shape(data, UnifiedExpressions.BrowOuterUpLeft), 4);
        Assert.Equal(0.12f, Shape(data, UnifiedExpressions.BrowLowererLeft), 4);
        Assert.Equal(0.06f, Shape(data, UnifiedExpressions.BrowPinchLeft), 4);
    }

    [Fact]
    public void BrowSync_OpenEyeIsIdentity()
    {
        var (mutator, _) = Create();
        mutator.browSync = true;
        mutator.browSyncStrength = 1f;
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 1f;
        data.Eye.Right.Openness = 1f;
        SetShape(data, UnifiedExpressions.BrowInnerUpLeft, 0.5f);

        mutator.MutateData(ref data);

        Assert.Equal(0.5f, Shape(data, UnifiedExpressions.BrowInnerUpLeft), 4);
        Assert.Equal(0f, Shape(data, UnifiedExpressions.BrowLowererLeft), 4);
    }

    [Fact]
    public void BrowSync_SidesAreIndependent()
    {
        var (mutator, _) = Create();
        mutator.browSync = true;
        mutator.browSyncStrength = 1f;
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = 0f;
        data.Eye.Right.Openness = 1f;
        SetShape(data, UnifiedExpressions.BrowInnerUpLeft, 0.5f);
        SetShape(data, UnifiedExpressions.BrowInnerUpRight, 0.5f);

        mutator.MutateData(ref data);

        Assert.Equal(0.3f, Shape(data, UnifiedExpressions.BrowInnerUpLeft), 4);
        Assert.Equal(0.5f, Shape(data, UnifiedExpressions.BrowInnerUpRight), 4);
        Assert.Equal(0f, Shape(data, UnifiedExpressions.BrowLowererRight), 4);
    }

    [Fact]
    public void Mutations_OrderIsDeterministic()
    {
        var names = TrackingMutation.GetImplementingMutations(true).Select(m => m.Name).ToArray();

        var correctors = Array.IndexOf(names, "Unified Correctors");
        var eyeAssists = Array.IndexOf(names, "Eye Assists");
        var mouthBrow = Array.IndexOf(names, "Mouth & Brow Assists");
        var filter = Array.IndexOf(names, "Data Filter");

        Assert.True(correctors >= 0 && eyeAssists >= 0 && mouthBrow >= 0 && filter >= 0);
        Assert.True(correctors < eyeAssists);
        Assert.True(eyeAssists < mouthBrow);
        Assert.True(mouthBrow < filter);
    }
}
