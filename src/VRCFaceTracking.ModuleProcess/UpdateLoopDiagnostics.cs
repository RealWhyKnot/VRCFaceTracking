using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.ModuleProcess;

internal sealed partial class UpdateLoopDiagnostics
{
    private static readonly double ToMs = 1000.0 / Stopwatch.Frequency;

    private readonly ILogger _logger;
    private readonly DiagStat _updateMs = new();
    private readonly DiagStat _captureMs = new();
    private readonly DiagStat _loopGapMs = new();
    private int _pushes;
    private long _lastLoop;
    private long _lastReport;

    public UpdateLoopDiagnostics(ILogger logger)
    {
        _logger = logger;
        _lastLoop = Stopwatch.GetTimestamp();
        _lastReport = _lastLoop;
    }

    public void CountPush() => _pushes++;

    public void Record(long loopStart, long updateEnd)
    {
        long now = Stopwatch.GetTimestamp();
        _updateMs.Add((updateEnd - loopStart) * ToMs);
        _captureMs.Add((now - updateEnd) * ToMs);
        _loopGapMs.Add((loopStart - _lastLoop) * ToMs);
        _lastLoop = loopStart;

        if ((now - _lastReport) * ToMs < 1000)
        {
            return;
        }

        _lastReport = now;
        (double uP50, double uP99) = _updateMs.Percentiles();
        (double cP50, double cP99) = _captureMs.Percentiles();
        (double gP50, double gP99) = _loopGapMs.Percentiles();
        LogDiagModuleUpdatesPushesRate(_logger, _updateMs.Count, _pushes, _updateMs.Count, uP50, uP99, _updateMs.Max, cP50, cP99, _captureMs.Max, gP50, gP99, _loopGapMs.Max);
        _updateMs.Reset();
        _captureMs.Reset();
        _loopGapMs.Reset();
        _pushes = 0;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.module updates={N} pushes={P} rate={Rate:N0}/s | Update p50={UP50:N2} p99={UP99:N2} max={UMax:N2}ms | capture p50={CP50:N3} p99={CP99:N3} max={CMax:N3}ms | loopGap p50={GP50:N2} p99={GP99:N2} max={GMax:N2}ms")]
    private static partial void LogDiagModuleUpdatesPushesRate(ILogger logger, int n, int p, int rate, double uP50, double uP99, double uMax, double cP50, double cP99, double cMax, double gP50, double gP99, double gMax);
}
