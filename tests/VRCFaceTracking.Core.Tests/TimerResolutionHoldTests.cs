using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Tests;

public sealed class TimerResolutionHoldTests
{
    private sealed class ListLogger : ILogger
    {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel} {formatter(state, exception)}");
    }

    private sealed class Fixture
    {
        public readonly ListLogger Log = new();
        public readonly TimerResolutionHold Hold;
        public bool RaiseSucceeds = true;
        public int Raises;
        public int Releases;

        public Fixture()
        {
            Hold = new TimerResolutionHold(Log, () =>
        {
            Raises++;
            return RaiseSucceeds;
        }, () => Releases++);
        }
    }

    private static readonly long Second = Stopwatch.Frequency;
    private static readonly long Idle = TimerResolutionHold.IdleSeconds * Stopwatch.Frequency;

    [Fact]
    public void NoTrackingDataNeverRaises()
    {
        var f = new Fixture();

        for (long t = 0L; t < 3 * Idle; t += Second)
        {
            f.Hold.Observe(t, active: false);
        }
        f.Hold.Release();

        Assert.Equal(0, f.Raises);
        Assert.Equal(0, f.Releases);
        Assert.Empty(f.Log.Lines);
    }

    [Fact]
    public void FlowingDataRaisesOnceAndHolds()
    {
        var f = new Fixture();

        for (long t = 0L; t < 3 * Idle; t += Second / 90)
        {
            f.Hold.Observe(t, active: true);
            f.Hold.Observe(t + 1, active: false);
        }

        Assert.Equal(1, f.Raises);
        Assert.Equal(0, f.Releases);
        Assert.Equal(["Information Raised timer resolution to 1 ms"], f.Log.Lines, StringComparer.Ordinal);
    }

    [Fact]
    public void IdleReleasesOnceAfterTimeout()
    {
        var f = new Fixture();
        f.Hold.Observe(5 * Second, active: true);

        f.Hold.Observe((5 * Second) + Idle - 1, active: false);
        Assert.Equal(0, f.Releases);

        f.Hold.Observe((5 * Second) + Idle, active: false);
        f.Hold.Observe((5 * Second) + (2 * Idle), active: false);
        Assert.Equal(1, f.Releases);
        Assert.Equal("Information Released 1 ms timer resolution", f.Log.Lines[^1]);
    }

    [Fact]
    public void DataAfterReleaseRaisesAgainOnFirstSample()
    {
        var f = new Fixture();
        f.Hold.Observe(0, active: true);
        f.Hold.Observe(Idle, active: false);

        f.Hold.Observe(Idle + Second, active: true);

        Assert.Equal(2, f.Raises);
        Assert.Equal(1, f.Releases);
        Assert.Equal(3, f.Log.Lines.Count);
    }

    [Fact]
    public void ReleaseBalancesOnlyASuccessfulRaise()
    {
        var f = new Fixture();
        f.Hold.Observe(0, active: true);

        f.Hold.Release();
        f.Hold.Release();

        Assert.Equal(1, f.Raises);
        Assert.Equal(1, f.Releases);
    }

    [Fact]
    public void FailedRaiseIsRetriedAndNeverReleased()
    {
        var f = new Fixture { RaiseSucceeds = false };

        f.Hold.Observe(0, active: true);
        f.Hold.Observe(Second, active: true);
        f.Hold.Observe(Second + Idle, active: false);
        f.Hold.Release();

        Assert.Equal(2, f.Raises);
        Assert.Equal(0, f.Releases);
        Assert.Empty(f.Log.Lines);
    }
}
