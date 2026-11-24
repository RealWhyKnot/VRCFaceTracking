using System.Diagnostics;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class ModuleStallTests
{
    private const double Tick = 0.025;
    private const float Rest = 0.7f;

    private static readonly long Second = Stopwatch.Frequency;
    private static readonly AvatarParameterSnapshot WideAvatar = new(usesNativeFaceTracking: false, ["v2/EyeWideLeft", "v2/EyeWideRight", "v2/EyeSquintLeft", "v2/EyeSquintRight"]);

    private static UnifiedTrackingData Lids(float left, float right)
    {
        var data = new UnifiedTrackingData();
        data.Eye.Left.Openness = left;
        data.Eye.Right.Openness = right;
        data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.3f;
        return data;
    }

    private static long FeedBlinks(SignalCapabilityTracker tracker, long start, double seconds)
    {
        long now = start;
        int samples = (int)(seconds * 60);
        for (int i = 0; i < samples; i++)
        {
            now = start + (i * Second / 60);
            float openness = i % 20 == 0 ? 0.1f : Rest;
            tracker.Observe(Lids(openness, openness), now);
        }

        return now;
    }

    [Fact]
    public void ModuleFlagsDecayAfterHalfASecondWithoutSamples()
    {
        var tracker = new SignalCapabilityTracker();
        long last = FeedBlinks(tracker, Stopwatch.GetTimestamp(), 2);
        Assert.True(tracker.Snapshot.ModuleEyeLeftLive);
        Assert.True(tracker.Snapshot.IsLive(UnifiedExpressions.JawOpen));
        int published = 0;
        tracker.Published += (_, _) => published++;

        tracker.Expire(last + (Second * 4 / 10));
        Assert.True(tracker.Snapshot.EyeLive);
        Assert.Equal(0, published);

        tracker.Expire(last + (Second * 6 / 10));
        SignalCapabilitySnapshot stale = tracker.Snapshot;
        Assert.False(stale.EyeLive);
        Assert.False(stale.ModuleEyeLeftLive);
        Assert.False(stale.IsLive(UnifiedExpressions.JawOpen));
        Assert.Equal(1, published);

        tracker.Expire(last + (Second * 2));
        Assert.Equal(1, published);
    }

    [Fact]
    public void ModuleFlagsComeBackOnTheFirstNewSample()
    {
        var tracker = new SignalCapabilityTracker();
        long last = FeedBlinks(tracker, Stopwatch.GetTimestamp(), 2);
        tracker.Expire(last + Second);
        Assert.False(tracker.Snapshot.EyeLive);

        tracker.Observe(Lids(0.4f, 0.4f), last + Second + 1);

        Assert.True(tracker.Snapshot.ModuleEyeLeftLive);
        Assert.True(tracker.Snapshot.IsLive(UnifiedExpressions.JawOpen));
    }

    [Fact]
    public void FreshExternalEyesStayLiveWhileTheModuleIsStale()
    {
        var tracker = new SignalCapabilityTracker();
        long last = FeedBlinks(tracker, Stopwatch.GetTimestamp(), 2);
        tracker.SetExternal(new bool[(int)UnifiedExpressions.Max], eyeLeft: true, eyeRight: false);

        tracker.Expire(last + Second);

        SignalCapabilitySnapshot snapshot = tracker.Snapshot;
        Assert.False(snapshot.ModuleEyeLeftLive);
        Assert.True(snapshot.EyeLeftLive);
        Assert.False(snapshot.EyeRightLive);
    }

    [Fact]
    public void NothingDecaysBeforeAModuleHasSentAnything()
    {
        var tracker = new SignalCapabilityTracker();
        int published = 0;
        tracker.Published += (_, _) => published++;

        tracker.Expire(Stopwatch.GetTimestamp() + (Second * 10));

        Assert.Equal(0, published);
    }

    [Theory]
    [InlineData(false, 0.5f)]
    [InlineData(true, 0f)]
    public void RelaxedLidsOfAStalledModuleDoNotWidenTheEyes(bool expire, float expectedWide)
    {
        var tracker = new SignalCapabilityTracker();
        long last = FeedBlinks(tracker, Stopwatch.GetTimestamp(), 3);
        Assert.InRange(tracker.Snapshot.EyeRestLeft, Rest - 0.01f, Rest + 0.01f);

        UnifiedTrackingData frozen = Lids(Rest, Rest);
        double now = 0;
        var fixes = new TrackingGlitchFixes { IsActive = true, QuietMouth = false, StuckEyelidFallback = false, RelaxOnFreeze = true, NowSeconds = () => now, RawInput = () => frozen };
        var fill = new SignalFillIn { IsActive = true, EyeLidFill = true, CurrentAvatar = () => WideAvatar, Capabilities = () => tracker.Snapshot };

        UnifiedTrackingData output = new();
        for (int i = 0; i < 200; i++)
        {
            now += Tick;
            if (expire)
            {
                tracker.Expire(last + (long)(now * Second));
            }

            output = new UnifiedTrackingData();
            output.CopyPropertiesOf(frozen);
            fixes.MutateData(ref output);
            fill.MutateData(ref output);
        }

        Assert.Equal(1f, output.Eye.Left.Openness, 3);
        Assert.Equal(1f, output.Eye.Right.Openness, 3);
        Assert.Equal(expectedWide, output.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight, 2);
        Assert.Equal(expectedWide, output.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight, 2);
    }

    [Fact]
    public async Task SenderDecaysModuleFlagsWhenTheModuleStopsSending()
    {
        var tracker = new SignalCapabilityTracker();
        await using var harness = new ParameterSenderServiceTests.Harness((_, _) => { }, capabilities: tracker);
        await harness.Sender.StartAsync(CancellationToken.None);

        long deadline = Stopwatch.GetTimestamp() + (Second * 5);
        int frame = 0;
        while (!tracker.Snapshot.ModuleEyeLeftLive && Stopwatch.GetTimestamp() < deadline)
        {
            using (UnifiedTracking.LockData())
            {
                UnifiedTracking.Data.Eye.Left.Openness = frame++ % 2 == 0 ? 0.2f : 0.8f;
            }

            UnifiedTracking.MarkDataUpdated();
            await Task.Delay(15);
        }

        Assert.True(tracker.Snapshot.ModuleEyeLeftLive);
        long stalled = Stopwatch.GetTimestamp();
        deadline = stalled + (Second * 5);
        while (tracker.Snapshot.ModuleEyeLeftLive && Stopwatch.GetTimestamp() < deadline)
        {
            await Task.Delay(20);
        }

        double seconds = Stopwatch.GetElapsedTime(stalled).TotalSeconds;
        await harness.Sender.StopAsync(CancellationToken.None);
        Assert.False(tracker.Snapshot.ModuleEyeLeftLive);
        Assert.InRange(seconds, 0.3, 2);
    }
}
