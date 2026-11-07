using System.Diagnostics;
using System.Text;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Tests;

public sealed class ShapeStatsTests
{
    private const int JawOpen = (int)UnifiedExpressions.JawOpen;
    private static readonly int GazeLeftX = ShapeStats.ShapeCount + 2;

    private static UnifiedTrackingData Frame(float jawOpen, float gazeX = 0)
    {
        var data = new UnifiedTrackingData();
        data.Shapes[JawOpen].Weight = jawOpen;
        data.Eye.Left.Gaze.x = gazeX;
        return data;
    }

    [Fact]
    public void ChannelNamesCoverEveryShapeAndBothEyes()
    {
        Assert.Equal(ShapeStats.ShapeCount + 6, ShapeStats.ChannelNames.Length);
        Assert.Equal("JawOpen", ShapeStats.ChannelNames[JawOpen]);
        Assert.Equal("EyeGazeLeftX", ShapeStats.ChannelNames[GazeLeftX]);
        Assert.DoesNotContain(ShapeStats.ChannelNames, name => string.Equals(name, "Max", StringComparison.Ordinal));
    }

    [Fact]
    public void TracksMinMeanMaxAndActiveShare()
    {
        var stats = new ShapeStats();
        foreach (float value in new[] { 0f, 0.02f, 0.5f, 1f })
        {
            stats.Add(Frame(value));
        }

        Assert.Equal(4, stats.Samples);
        Assert.Equal(0f, stats.Min(JawOpen));
        Assert.Equal(1f, stats.Max(JawOpen));
        Assert.Equal(0.38, stats.Mean(JawOpen), 3);
        Assert.Equal(50, stats.ActivePercent(JawOpen), 3);
    }

    [Fact]
    public void PercentileLandsWithinOneBinOfTheTrueValue()
    {
        var stats = new ShapeStats();
        for (int i = 0; i < 100; i++)
        {
            stats.Add(Frame(i / 100f, (i / 50f) - 1f));
        }

        Assert.InRange(stats.Percentile(JawOpen, 0.95), 0.94, 0.94 + (2.0 / ShapeStats.Bins));
        Assert.InRange(stats.Percentile(GazeLeftX, 0.5), -0.02, -0.02 + (2.0 / ShapeStats.Bins));
        Assert.Equal(stats.Max(JawOpen), stats.Percentile(JawOpen, 1));
    }

    [Fact]
    public void SkipsNonFiniteValuesAndCountsThem()
    {
        var stats = new ShapeStats();
        stats.Add(Frame(float.NaN));
        stats.Add(Frame(0.4f));

        Assert.Equal(1, stats.Count(JawOpen));
        Assert.Equal(0.4f, stats.Max(JawOpen));
        var moving = new StringBuilder();
        stats.AppendSummary(moving, new StringBuilder());
        Assert.Contains("JawOpen=0.40/0.40/0.40/0.40/100%/nan=1", moving.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SummarySplitsMovingChannelsFromZeroOnes()
    {
        var stats = new ShapeStats();
        stats.Add(Frame(0.25f));

        var moving = new StringBuilder();
        var zero = new StringBuilder();
        stats.AppendSummary(moving, zero);

        Assert.Contains("JawOpen=0.25/0.25/0.25/0.25/100%", moving.ToString(), StringComparison.Ordinal);
        Assert.Contains("EyeOpennessLeft=1.00", moving.ToString(), StringComparison.Ordinal);
        Assert.Contains("TongueOut", zero.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("JawOpen", zero.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var stats = new ShapeStats();
        stats.Add(Frame(0.7f));
        stats.Reset();

        Assert.Equal(0, stats.Samples);
        Assert.True(stats.IsZero(JawOpen));
        Assert.Equal(0f, stats.Max(JawOpen));
    }

    [Fact]
    public void LogWritesRawAndOutputLinesOncePerInterval()
    {
        var lines = new List<string>();
        var log = new ShapeStatsLog(lines.Add, TimeSpan.FromSeconds(30));
        long start = 1_000_000;
        long step = Stopwatch.Frequency;

        for (int second = 0; second < 30; second++)
        {
            log.RecordRaw(Frame(0.8f));
            log.RecordOutput(start + (second * step), Frame(0.6f));
        }
        Assert.Empty(lines);

        log.RecordRaw(Frame(0.8f));
        log.RecordOutput(start + (30 * step), Frame(0.6f));

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("diag.shapes raw samples=31 over 30s", lines[0], StringComparison.Ordinal);
        Assert.Contains("JawOpen=0.80/", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("diag.shapes out samples=31 over 30s", lines[1], StringComparison.Ordinal);
        Assert.Contains("JawOpen=0.60/", lines[1], StringComparison.Ordinal);
        Assert.False(log.HasSamples);
    }

    [Fact]
    public void ReportWithNoSamplesWritesNothing()
    {
        var lines = new List<string>();
        var log = new ShapeStatsLog(lines.Add);
        log.Report(Stopwatch.GetTimestamp());
        Assert.Empty(lines);
    }

    [Theory]
    [InlineData(LoggingSettings.ShapeLoggingFollowBuild, BuildChannel.Dev, true)]
    [InlineData(LoggingSettings.ShapeLoggingFollowBuild, BuildChannel.Beta, true)]
    [InlineData(LoggingSettings.ShapeLoggingFollowBuild, BuildChannel.Release, false)]
    [InlineData(LoggingSettings.ShapeLoggingOn, BuildChannel.Release, true)]
    [InlineData(LoggingSettings.ShapeLoggingOff, BuildChannel.Beta, false)]
    [InlineData(7, BuildChannel.Release, false)]
    public void ShapeLoggingDefaultsOnOutsideReleaseBuilds(int choice, BuildChannel channel, bool expected) =>
        Assert.Equal(expected, LoggingSettings.IsShapeLoggingOn(choice, channel));

    [Fact]
    public void NewSettingsFollowTheBuild() =>
        Assert.Equal(LoggingSettings.ShapeLoggingFollowBuild, new LoggingSettings().ShapeLoggingChoice);
}
