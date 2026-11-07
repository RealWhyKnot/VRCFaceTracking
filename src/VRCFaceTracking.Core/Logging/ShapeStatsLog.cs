using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Logging;

public sealed class ShapeStatsLog
{
    public const string Category = "VRCFaceTracking.Core.Logging.ShapeStatsLog";
    public static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);

    private readonly Action<string> _writeLine;
    private readonly long _intervalTicks;
    private readonly ShapeStats _raw = new();
    private readonly ShapeStats _output = new();
    private readonly StringBuilder _moving = new(8192);
    private readonly StringBuilder _zero = new(2048);
    private readonly StringBuilder _line = new(10240);
    private long _windowStart;

    public ShapeStatsLog(Action<string> writeLine)
        : this(writeLine, ReportInterval)
    {
    }

    public ShapeStatsLog(Action<string> writeLine, TimeSpan interval)
    {
        _writeLine = writeLine;
        _intervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);
    }

    public static ShapeStatsLog ToFile(FileLoggerProvider fileLog) =>
        new(line => fileLog.Write(LogLevel.Information, FileLogger.FormatLine(Category, LogLevel.Information, line, exception: null, fileLog.TimeProvider.GetLocalNow().DateTime)));

    public bool HasSamples => _raw.Samples > 0 || _output.Samples > 0;

    public void RecordRaw(UnifiedTrackingData data) => _raw.Add(data);

    public void RecordOutput(long ticks, UnifiedTrackingData data)
    {
        if (_windowStart == 0)
        {
            _windowStart = ticks;
        }

        _output.Add(data);
        if (ticks - _windowStart >= _intervalTicks)
        {
            Report(ticks);
        }
    }

    public void Report(long ticks)
    {
        if (HasSamples)
        {
            double seconds = Math.Max(0, ticks - _windowStart) / (double)Stopwatch.Frequency;
            Write("raw", _raw, seconds);
            Write("out", _output, seconds);
        }

        _raw.Reset();
        _output.Reset();
        _windowStart = ticks;
    }

    private void Write(string stage, ShapeStats stats, double seconds)
    {
        _moving.Clear();
        _zero.Clear();
        _line.Clear();
        stats.AppendSummary(_moving, _zero);
        _line.Append("diag.shapes ").Append(stage)
            .Append(" samples=").Append(stats.Samples)
            .Append(" over ").Append(seconds.ToString("0", CultureInfo.InvariantCulture)).Append('s')
            .Append(" min/mean/p95/max/active | ").Append(_moving)
            .Append(" | zero: ");
        if (_zero.Length == 0)
        {
            _line.Append("none");
        }
        else
        {
            _line.Append(_zero);
        }
        _writeLine(_line.ToString());
    }
}
