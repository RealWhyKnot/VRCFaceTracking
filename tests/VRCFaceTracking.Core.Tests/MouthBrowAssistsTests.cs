using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed class MouthBrowAssistsTests
{
    private sealed class FakeClock
    {
        public double Now;
    }

    private static (MouthBrowAssists mutator, FakeClock clock) Create()
    {
        var clock = new FakeClock();
        var mutator = new MouthBrowAssists
        {
            NowSeconds = () => clock.Now,
        };
        return (mutator, clock);
    }

    private static float Shape(UnifiedTrackingData data, UnifiedExpressions shape) => data.Shapes[(int)shape].Weight;

    private static void SetShape(UnifiedTrackingData data, UnifiedExpressions shape, float weight) => data.Shapes[(int)shape].Weight = weight;

    private static UnifiedTrackingData IdleCandidateData(float jaw = 0.15f, float mouthClosed = 0f, float activity = 0f)
    {
        var data = new UnifiedTrackingData();
        SetShape(data, UnifiedExpressions.JawOpen, jaw);
        SetShape(data, UnifiedExpressions.MouthClosed, mouthClosed);
        SetShape(data, UnifiedExpressions.MouthCornerPullLeft, activity);
        return data;
    }

    [Fact]
    public void IdleMouthAutoCloseClosesAfterDwell()
    {
        (MouthBrowAssists? mutator, FakeClock? clock) = Create();
        mutator.IdleMouthAutoClose = true;
        mutator.IdleCloseStrength = 0.5f;

        for (int i = 0; i <= 11; i++)
        {
            clock.Now = i * 0.1;
            UnifiedTrackingData frame = IdleCandidateData();
            mutator.MutateData(ref frame);
            Assert.Equal(0.15f, Shape(frame, UnifiedExpressions.JawOpen), 4);
        }

        clock.Now = 1.21;
        UnifiedTrackingData data = IdleCandidateData();
        mutator.MutateData(ref data);
        Assert.Equal(0.075f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Theory]
    [InlineData(0.30f, 0f, 0f)]
    [InlineData(0.15f, 0.25f, 0f)]
    [InlineData(0.15f, 0f, 0.25f)]
    public void IdleMouthAutoCloseIgnoresNonCandidateFrames(float jaw, float mouthClosed, float activity)
    {
        (MouthBrowAssists? mutator, FakeClock? clock) = Create();
        mutator.IdleMouthAutoClose = true;
        mutator.IdleCloseStrength = 0.5f;

        for (int i = 0; i <= 14; i++)
        {
            clock.Now = i * 0.1;
            UnifiedTrackingData frame = IdleCandidateData(jaw, mouthClosed, activity);
            mutator.MutateData(ref frame);
            Assert.Equal(jaw, Shape(frame, UnifiedExpressions.JawOpen), 4);
        }
    }

    [Fact]
    public void IdleMouthAutoCloseNonCandidateFrameResetsDwell()
    {
        (MouthBrowAssists? mutator, FakeClock? clock) = Create();
        mutator.IdleMouthAutoClose = true;
        mutator.IdleCloseStrength = 0.5f;

        for (int i = 0; i <= 6; i++)
        {
            clock.Now = i * 0.1;
            UnifiedTrackingData frame = IdleCandidateData();
            mutator.MutateData(ref frame);
        }

        clock.Now = 0.7;
        UnifiedTrackingData busy = IdleCandidateData(activity: 0.5f);
        mutator.MutateData(ref busy);

        for (int i = 8; i <= 19; i++)
        {
            clock.Now = i * 0.1;
            UnifiedTrackingData frame = IdleCandidateData();
            mutator.MutateData(ref frame);
            Assert.Equal(0.15f, Shape(frame, UnifiedExpressions.JawOpen), 4);
        }

        clock.Now = 2.01;
        UnifiedTrackingData data = IdleCandidateData();
        mutator.MutateData(ref data);
        Assert.Equal(0.075f, Shape(data, UnifiedExpressions.JawOpen), 4);
    }

    [Fact]
    public void MutationsOrderIsDeterministic()
    {
        string[] names = [.. TrackingMutation.GetImplementingMutations(ordered: true).Select(m => m.Name)];

        int correctors = Array.IndexOf(names, "Unified Correctors");
        int eyeAssists = Array.IndexOf(names, "Eye Assists");
        int mouthBrow = Array.IndexOf(names, "Mouth & Brow Assists");
        int filter = Array.IndexOf(names, "Data Filter");

        Assert.True(correctors >= 0 && eyeAssists >= 0 && mouthBrow >= 0 && filter >= 0);
        Assert.True(correctors < eyeAssists);
        Assert.True(eyeAssists < mouthBrow);
        Assert.True(mouthBrow < filter);
    }

    [Fact]
    public void AssistsAreInactiveByDefault()
    {
        TrackingMutation[] mutations = TrackingMutation.GetImplementingMutations(ordered: true);

        Assert.False(mutations.Single(m => string.Equals(m.Name, "Eye Assists", StringComparison.Ordinal)).IsActive);
        Assert.False(mutations.Single(m => string.Equals(m.Name, "Mouth & Brow Assists", StringComparison.Ordinal)).IsActive);
    }
}
