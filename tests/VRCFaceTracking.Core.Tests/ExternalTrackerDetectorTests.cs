using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

public sealed class ExternalTrackerDetectorTests
{
    private static string[] Names(IReadOnlyList<DetectedExternalTracker> found) => [.. found.Select(f => f.Name)];

    [Theory]
    [InlineData("Baballonia")]
    [InlineData("Baballonia.Desktop")]
    [InlineData("BabbleApp")]
    [InlineData("babbleapp.exe")]
    [InlineData("BABALLONIA.DESKTOP")]
    public void BaballoniaProcessesAreDetected(string process)
    {
        IReadOnlyList<DetectedExternalTracker> found = ExternalTrackerDetector.Detect([process]);

        DetectedExternalTracker only = Assert.Single(found);
        Assert.Equal(ExternalTrackerKind.Baballonia, only.Kind);
        Assert.Equal("Baballonia", only.Name);
    }

    [Theory]
    [InlineData("EyeTrackApp")]
    [InlineData("EyeTrackVR")]
    [InlineData("eyetrackvr.exe")]
    public void EyeTrackVrProcessesAreDetected(string process)
    {
        DetectedExternalTracker only = Assert.Single(ExternalTrackerDetector.Detect([process]));
        Assert.Equal(ExternalTrackerKind.EyeTrackVr, only.Kind);
        Assert.Equal("EyeTrackVR", only.Name);
    }

    [Theory]
    [InlineData("Babble-Frame-Runtime")]
    [InlineData("frameeyeosc")]
    [InlineData("vrcft-steam-frame")]
    public void SteamFrameProcessesAreDetected(string process)
    {
        DetectedExternalTracker only = Assert.Single(ExternalTrackerDetector.Detect([process]));
        Assert.Equal(ExternalTrackerKind.SteamFrame, only.Kind);
        Assert.Equal("Steam Frame", only.Name);
    }

    [Fact]
    public void UnrelatedProcessesAreIgnored() =>
        Assert.Empty(ExternalTrackerDetector.Detect(["explorer", "chrome", "VRChat", "BabbleAppTools", "notbaballonia"]));

    [Fact]
    public void DuplicateProcessesCollapseAndOrderIsStable()
    {
        IReadOnlyList<DetectedExternalTracker> found = ExternalTrackerDetector.Detect(["frameeyeosc", "Baballonia", "BabbleApp", "Baballonia.Desktop", "EyeTrackApp"]);

        Assert.Equal(["Baballonia", "EyeTrackVR", "Steam Frame"], Names(found));
    }

    [Fact]
    public void RefreshReadsTheProviderAndReportsChanges()
    {
        string[] running = ["Baballonia"];
        var detector = new ExternalTrackerDetector(() => running, new ManualClock());
        Assert.Empty(detector.Current);

        Assert.True(detector.Refresh());
        Assert.Equal(["Baballonia"], Names(detector.Current));
        Assert.False(detector.Refresh());

        running = [];
        Assert.True(detector.Refresh());
        Assert.Empty(detector.Current);
    }

    [Fact]
    public async Task PollIsRateLimitedByTheInterval()
    {
        int calls = 0;
        var clock = new ManualClock();
        var detector = new ExternalTrackerDetector(
            () =>
            {
                Interlocked.Increment(ref calls);
                return ["Baballonia"];
            },
            clock,
            TimeSpan.FromSeconds(5));

        detector.Poll();
        await WaitForAsync(() => Volatile.Read(ref calls) == 1);
        Assert.Equal(["Baballonia"], Names(detector.Current));

        for (int i = 0; i < 20; i++)
        {
            detector.Poll();
        }

        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref calls));

        clock.Advance(TimeSpan.FromSeconds(6));
        detector.Poll();
        await WaitForAsync(() => Volatile.Read(ref calls) == 2);
    }

    [Fact]
    public async Task SlowProvidersAreNeverRunTwiceAtOnce()
    {
        int active = 0;
        int peak = 0;
        using var gate = new ManualResetEventSlim();
        var clock = new ManualClock();
        var detector = new ExternalTrackerDetector(
            () =>
            {
                int now = Interlocked.Increment(ref active);
                peak = Math.Max(peak, now);
                gate.Wait(TimeSpan.FromSeconds(5));
                Interlocked.Decrement(ref active);
                return [];
            },
            clock,
            TimeSpan.Zero);

        detector.Poll();
        detector.Poll();
        detector.Poll();
        await Task.Delay(100);
        gate.Set();
        await WaitForAsync(() => Volatile.Read(ref active) == 0);

        Assert.Equal(1, peak);
    }

    [Fact]
    public void TheDefaultProviderReadsProcessesWithoutThrowing()
    {
        IReadOnlyList<string> names = ExternalTrackerDetector.ReadRunningProcessNames();
        Assert.NotNull(names);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
