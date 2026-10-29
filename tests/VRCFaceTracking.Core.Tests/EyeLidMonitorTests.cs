using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Core.Tests;

public sealed class EyeLidMonitorTests
{
    private sealed class ListLogger : ILogger
    {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private const double FrameMs = 1000.0 / 90.0;

    private sealed class Feed
    {
        private readonly EyeLidMonitor _monitor;
        private long _ticks = 1_000_000;
        private int _frame;

        public Feed(EyeLidMonitor monitor)
        {
            _monitor = monitor;
        }

        public void Frames(int count, Func<int, (float rawL, float rawR, float outL, float outR)> sample)
        {
            for (int i = 0; i < count; i++)
            {
                (float rawL, float rawR, float outL, float outR) = sample(_frame++);
                _monitor.Observe(_ticks, rawL, rawR, outL, outR);
                _ticks += (long)(FrameMs * Stopwatch.Frequency / 1000.0);
            }
        }
    }

    private static float Jitter(int frame, float center) => center + (frame % 7 * 0.001f);

    private static (ListLogger log, Feed feed) Create()
    {
        var log = new ListLogger();
        return (log, new Feed(new EyeLidMonitor(log)));
    }

    [Fact]
    public void NormalBlinkIsSilent()
    {
        (ListLogger? log, Feed? feed) = Create();

        feed.Frames(45, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));
        feed.Frames(14, f => (Jitter(f, 0.05f), Jitter(f, 0.05f), 0f, 0f));
        feed.Frames(45, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));

        Assert.Empty(log.Lines);
    }

    [Fact]
    public void FrozenLidIsReportedAsHeld()
    {
        (ListLogger? log, Feed? feed) = Create();

        feed.Frames(27, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));
        feed.Frames(40, _ => (0.35f, 0.36f, 0.05f, 0.05f));
        feed.Frames(27, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));

        string held = Assert.Single(log.Lines, l => l.StartsWith("diag.eye held", StringComparison.Ordinal));
        Assert.Contains("L=0.350 R=0.360", held, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.StartsWith("diag.eye closed", StringComparison.Ordinal) && l.Contains("held 39/40", StringComparison.Ordinal));
    }

    [Fact]
    public void ClosedOutputWithOpenRawEyesIsReported()
    {
        (ListLogger? log, Feed? feed) = Create();

        feed.Frames(18, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));
        feed.Frames(45, f => (Jitter(f, 0.85f), Jitter(f, 0.85f), 0.1f, 0.1f));
        feed.Frames(18, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));

        string closed = Assert.Single(log.Lines);
        Assert.StartsWith("diag.eye closed", closed, StringComparison.Ordinal);
        Assert.Contains("raw L 0.85-0.86", closed, StringComparison.Ordinal);
        Assert.Contains("held 0/", closed, StringComparison.Ordinal);
    }

    [Fact]
    public void WideOpenLidsClampedAtOneAreNotHeld()
    {
        (ListLogger? log, Feed? feed) = Create();

        feed.Frames(90, _ => (1f, 1f, 1f, 1f));
        feed.Frames(9, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));

        Assert.Empty(log.Lines);
    }

    [Fact]
    public void FullyClosedLidsAtZeroAreClosedButNotHeld()
    {
        (ListLogger? log, Feed? feed) = Create();

        feed.Frames(18, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));
        feed.Frames(54, _ => (0f, 0f, 0f, 0f));
        feed.Frames(18, f => (Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f), Jitter(f, 0.9f)));

        string closed = Assert.Single(log.Lines);
        Assert.StartsWith("diag.eye closed", closed, StringComparison.Ordinal);
        Assert.Contains("held 0/", closed, StringComparison.Ordinal);
    }
}
