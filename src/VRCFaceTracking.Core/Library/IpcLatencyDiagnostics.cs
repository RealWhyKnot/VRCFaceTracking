using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Library;

internal sealed partial class IpcLatencyDiagnostics
{
    private const double SlowSampleMs = 50;
    private const double MaxPlausibleLegMs = 10_000;

    private readonly ILogger _logger;
    private readonly DiagStat _module = new();
    private readonly DiagStat _transit = new();
    private long _lastReport;
    private long _unstamped;
    private long _keyframes;

    public IpcLatencyDiagnostics(ILogger logger)
    {
        _logger = logger;
    }

    public void Record(ReplyUpdatePacket reply, bool accepted)
    {
        long now = Stopwatch.GetTimestamp();
        double toMs = 1000.0 / Stopwatch.Frequency;
        if (_lastReport == 0)
        {
            _lastReport = now;
        }

        if (!accepted)
        {
            _keyframes++;
        }
        else if (reply.SampleTicks == 0 || reply.ReplySentTicks == 0)
        {
            _unstamped++;
        }
        else
        {
            double moduleMs = (reply.ReplySentTicks - reply.SampleTicks) * toMs;
            double transitMs = (now - reply.ReplySentTicks) * toMs;
            if (moduleMs >= 0 && transitMs >= 0 && moduleMs + transitMs < MaxPlausibleLegMs)
            {
                _module.Add(moduleMs);
                _transit.Add(transitMs);
                if (moduleMs + transitMs > SlowSampleMs)
                {
                    LogDiagIpcSlowSampleMs(_logger, moduleMs + transitMs, moduleMs, transitMs);
                }
            }
            else
            {
                _unstamped++;
            }
        }

        if ((now - _lastReport) * toMs < 1000)
        {
            return;
        }

        _lastReport = now;
        (double aP50, double aP99) = _module.Percentiles();
        (double bP50, double bP99) = _transit.Percentiles();
        long nonFinite = Interlocked.Exchange(ref ReplyUpdatePacket.NonFiniteRejected, 0);
        LogDiagIpcSamplesKeyframesUnstamped(_logger, _module.Count, _keyframes, _unstamped, nonFinite, aP50, aP99, _module.Max, bP50, bP99, _transit.Max);
        _module.Reset();
        _transit.Reset();
        _unstamped = 0;
        _keyframes = 0;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.ipc SLOW sample {Total:N1}ms: module {A:N1} transit {B:N1}")]
    private static partial void LogDiagIpcSlowSampleMs(ILogger logger, double total, double a, double b);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.ipc samples={N} keyframes={K} unstamped={U} nonFinite={F} | module p50={AP50:N2} p99={AP99:N2} max={AMax:N2}ms | transit p50={BP50:N2} p99={BP99:N2} max={BMax:N2}ms")]
    private static partial void LogDiagIpcSamplesKeyframesUnstamped(ILogger logger, int n, long k, long u, long f, double aP50, double aP99, double aMax, double bP50, double bP99, double bMax);
}
