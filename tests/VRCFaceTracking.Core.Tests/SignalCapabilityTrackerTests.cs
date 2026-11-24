using System.Diagnostics;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class SignalCapabilityTrackerTests
{
    private static readonly long Second = Stopwatch.Frequency;

    private static UnifiedTrackingData Frame(float cheek = 0f, float eyeOpen = 1f)
    {
        var data = new UnifiedTrackingData();
        data.Shapes[(int)UnifiedExpressions.CheekSquintLeft].Weight = cheek;
        data.Eye.Left.Openness = eyeOpen;
        data.Eye.Right.Openness = 1f;
        return data;
    }

    private static void Feed(SignalCapabilityTracker tracker, Func<int, UnifiedTrackingData> frame, int seconds)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i <= seconds * 10; i++)
        {
            tracker.Observe(frame(i), start + (i * Second / 10));
        }
    }

    [Fact]
    public void ActiveShapeIsLiveAndZeroShapeIsNot()
    {
        var tracker = new SignalCapabilityTracker();

        Feed(tracker, i => Frame(cheek: i % 2 == 0 ? 0.4f : 0.1f), seconds: 2);

        SignalCapabilitySnapshot snapshot = tracker.Snapshot;
        Assert.True(snapshot.HasData);
        Assert.True(snapshot.IsLive(UnifiedExpressions.CheekSquintLeft));
        Assert.False(snapshot.IsLive(UnifiedExpressions.CheekSquintRight));
        Assert.False(snapshot.IsLive(UnifiedExpressions.TongueOut));
        Assert.True(snapshot.IsLive(UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.CheekSquintRight));
    }

    [Fact]
    public void EyeIsLiveOnlyWhenOpennessMoves()
    {
        var idle = new SignalCapabilityTracker();
        Feed(idle, _ => Frame(), seconds: 2);
        Assert.False(idle.Snapshot.EyeLive);

        var blinking = new SignalCapabilityTracker();
        Feed(blinking, i => Frame(eyeOpen: i % 5 == 0 ? 0.2f : 0.9f), seconds: 2);
        Assert.True(blinking.Snapshot.EyeLeftLive);
        Assert.False(blinking.Snapshot.EyeRightLive);
        Assert.InRange(blinking.Snapshot.EyeRestLeft, 0.8f, 1f);
    }

    [Fact]
    public void PublishesAboutOncePerSecond()
    {
        var tracker = new SignalCapabilityTracker();
        int published = 0;
        tracker.Published += (_, _) => published++;

        Feed(tracker, _ => Frame(cheek: 0.5f), seconds: 3);

        Assert.InRange(published, 2, 4);
    }

    [Fact]
    public void SnapshotsAreImmutableOnceHandedOut()
    {
        var tracker = new SignalCapabilityTracker();
        Feed(tracker, _ => Frame(cheek: 0.5f), seconds: 2);
        SignalCapabilitySnapshot first = tracker.Snapshot;
        long version = first.Version;

        Feed(tracker, _ => Frame(), seconds: 25);

        Assert.True(first.IsLive(UnifiedExpressions.CheekSquintLeft));
        Assert.Equal(version, first.Version);
        Assert.NotSame(first, tracker.Snapshot);
        Assert.False(tracker.Snapshot.IsLive(UnifiedExpressions.CheekSquintLeft));
    }

    [Fact]
    public void ResetClearsEverythingAndPublishes()
    {
        var tracker = new SignalCapabilityTracker();
        Feed(tracker, _ => Frame(cheek: 0.5f), seconds: 2);
        SignalCapabilitySnapshot? seen = null;
        tracker.Published += (_, e) => seen = e.Snapshot;

        tracker.Reset();

        Assert.False(tracker.Snapshot.HasData);
        Assert.Equal(0, tracker.Snapshot.LiveShapeCount);
        Assert.False(tracker.Snapshot.EyeLive);
        Assert.Same(tracker.Snapshot, seen);
    }

    [Fact]
    public async Task RunsWhileShapeFileLoggingIsOff()
    {
        var tracker = new SignalCapabilityTracker();
        await using var harness = new ParameterSenderServiceTests.Harness((_, _) => { }, capabilities: tracker);
        await harness.Sender.StartAsync(CancellationToken.None);
        using (UnifiedTracking.LockData())
        {
            UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.CheekSquintLeft].Weight = 0.6f;
        }

        long deadline = Stopwatch.GetTimestamp() + (Second * 5);
        SignalCapabilitySnapshot seen = tracker.Snapshot;
        while (!seen.IsLive(UnifiedExpressions.CheekSquintLeft) && Stopwatch.GetTimestamp() < deadline)
        {
            UnifiedTracking.MarkDataUpdated();
            await Task.Delay(20);
            seen = tracker.Snapshot;
        }

        await harness.Sender.StopAsync(CancellationToken.None);
        Assert.True(seen.IsLive(UnifiedExpressions.CheekSquintLeft));
    }
}
