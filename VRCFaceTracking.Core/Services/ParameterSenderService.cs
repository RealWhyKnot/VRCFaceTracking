using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Services;

public class ParameterSenderService : BackgroundService
{
    // We probably don't need a queue since we use osc message bundles, but for now, we're keeping it as
    // we might want to allow a way for the user to specify bundle or single message sends in the future
    private const int TickIntervalMs = 10;
    private const double MinDataIntervalMs = 5;
    private const double RefreshIntervalMs = 25;
    private const double StallTickGapMs = 50;
    private const double SlowSendMs = 5;
    private const double StaleDataMs = 250;

    private readonly DiagStat _tickGap = new();
    private readonly DiagStat _sendMs = new();
    private readonly DiagStat _batchSize = new();
    private readonly DiagStat _sampleAgeMs = new();
    private long _diagStalls;
    private long _diagMessages;
    private long _staleSinceTicks;
    private TimeSpan _lastGcPause;
    private TimeSpan _lastReportGcPause;
    private int _lastGen0, _lastGen1, _lastGen2;

    private static readonly ConcurrentQueue<OscMessage> SendQueue = new();
    private readonly List<OscMessage> _batch = new();

    private readonly OscSendService _sendService;
    private readonly ILogger<ParameterSenderService> _logger;
    private readonly UnifiedTrackingMutator _mutator;
    private readonly EyeLidMonitor _eyeLids;

    public static bool AllParametersRelevantStatic
    {
        get; set;
    }
    public bool AllParametersRelevant
    {
        get => AllParametersRelevantStatic;
        set
        {
            if (AllParametersRelevantStatic == value) return;
            AllParametersRelevantStatic = value;
            SendQueue.Clear();
            foreach (var parameter in UnifiedTracking.AllParameters)
            {
                parameter.ResetParam(Array.Empty<IParameterDefinition>());
            }

            UnifiedTracking.MarkDataUpdated();
        }
    }

    public ParameterSenderService(OscSendService sendService, UnifiedTrackingMutator mutator, ILogger<ParameterSenderService> logger)
    {
        _sendService = sendService;
        _logger = logger;
        _mutator = mutator;
        _eyeLids = new EyeLidMonitor(logger);
    }

    private void ReportDiagnostics()
    {
        var (gapP50, gapP99) = _tickGap.Percentiles();
        var (sendP50, sendP99) = _sendMs.Percentiles();
        var (batchP50, _) = _batchSize.Percentiles();
        var (ageP50, ageP99) = _sampleAgeMs.Percentiles();

        var gcPause = GC.GetTotalPauseDuration();
        _logger.LogDebug(
            "diag.send ticks={Ticks} gap p50={GapP50:N1} p99={GapP99:N1} max={GapMax:N1}ms | batch p50={BatchP50:N0} max={BatchMax:N0} msgs={Msgs} | send p50={SendP50:N2} p99={SendP99:N2} max={SendMax:N2}ms | sampleAge p50={AgeP50:N1} p99={AgeP99:N1} max={AgeMax:N1}ms | queue={Queue} stalls={Stalls} gcPause={GcPause:N1}ms",
            _tickGap.Count, gapP50, gapP99, _tickGap.Max,
            batchP50, _batchSize.Max, _diagMessages,
            sendP50, sendP99, _sendMs.Max,
            ageP50, ageP99, _sampleAgeMs.Max,
            SendQueue.Count, _diagStalls, (gcPause - _lastReportGcPause).TotalMilliseconds);
        _lastReportGcPause = gcPause;

        _tickGap.Reset();
        _sendMs.Reset();
        _batchSize.Reset();
        _sampleAgeMs.Reset();
        _diagMessages = 0;
    }

    public static void Enqueue(OscMessage message) => SendQueue.Enqueue(message);
    public static void Clear() => SendQueue.Clear();

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                Run(cancellationToken);
            }
            finally
            {
                done.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "OSC Send",
        };
        thread.Start();
        return done.Task;
    }

    private void Run(CancellationToken cancellationToken)
    {
        var wakeHandles = new[] { UnifiedTracking.DataArrived, cancellationToken.WaitHandle };
        var lastDataVersion = -1;
        var toMs = 1000.0 / Stopwatch.Frequency;
        var lastTick = Stopwatch.GetTimestamp();
        var lastReport = lastTick;
        var lastUpdate = lastTick;
        var lastDataUpdate = 0L;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                WaitHandle.WaitAny(wakeHandles, TickIntervalMs);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var tickNow = Stopwatch.GetTimestamp();
                var gapMs = (tickNow - lastTick) * toMs;
                lastTick = tickNow;

                var diag = _logger.IsEnabled(LogLevel.Debug);
                if (diag)
                {
                    _tickGap.Add(gapMs);

                    var gcPause = GC.GetTotalPauseDuration();
                    var gen0 = GC.CollectionCount(0);
                    var gen1 = GC.CollectionCount(1);
                    var gen2 = GC.CollectionCount(2);
                    if (gapMs > StallTickGapMs)
                    {
                        _diagStalls++;
                        _logger.LogDebug("diag.send STALL tick gap {Gap:N1}ms (expected {Expected}ms), queue {Queue} | gcPause {GcPause:N1}ms gcs {Gen0}/{Gen1}/{Gen2} | threadPool pending {Pending} threads {Threads}",
                            gapMs, TickIntervalMs, SendQueue.Count,
                            (gcPause - _lastGcPause).TotalMilliseconds, gen0 - _lastGen0, gen1 - _lastGen1, gen2 - _lastGen2,
                            ThreadPool.PendingWorkItemCount, ThreadPool.ThreadCount);
                    }
                    _lastGcPause = gcPause;
                    _lastGen0 = gen0;
                    _lastGen1 = gen1;
                    _lastGen2 = gen2;

                    var updateTicks = UnifiedTracking.LastDataUpdateTicks;
                    if (updateTicks != 0)
                    {
                        var ageMs = (tickNow - updateTicks) * toMs;
                        if (ageMs > StaleDataMs && _staleSinceTicks == 0)
                        {
                            _staleSinceTicks = updateTicks;
                            _logger.LogDebug("diag.send no new tracking data for {Age:N0}ms", ageMs);
                        }
                        else if (ageMs <= StaleDataMs && _staleSinceTicks != 0)
                        {
                            _logger.LogDebug("diag.send tracking data resumed after {Gap:N0}ms", (updateTicks - _staleSinceTicks) * toMs);
                            _staleSinceTicks = 0;
                        }
                    }

                    if ((tickNow - lastReport) * toMs >= 1000)
                    {
                        lastReport = tickNow;
                        ReportDiagnostics();
                    }
                }

                if (UnifiedTracking.DataVersion != lastDataVersion)
                {
                    var sinceDataMs = (Stopwatch.GetTimestamp() - lastDataUpdate) * toMs;
                    if (sinceDataMs < MinDataIntervalMs)
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(MinDataIntervalMs - sinceDataMs));
                    }

                    lastDataVersion = UnifiedTracking.DataVersion;
                    UnifiedTracking.UpdateData();
                    lastDataUpdate = lastUpdate = Stopwatch.GetTimestamp();

                    if (diag)
                    {
                        _sampleAgeMs.Add((lastDataUpdate - UnifiedTracking.LastSampleTicks) * toMs);
                        var eye = _mutator.LastEyeOpenness;
                        _eyeLids.Observe(lastDataUpdate, eye.RawLeft, eye.RawRight, eye.OutLeft, eye.OutRight);
                    }
                }
                else if ((tickNow - lastUpdate) * toMs >= RefreshIntervalMs)
                {
                    UnifiedTracking.UpdateData();
                    lastUpdate = Stopwatch.GetTimestamp();
                }

                if (SendQueue.IsEmpty)
                {
                    continue;
                }

                _batch.Clear();
                while (SendQueue.TryDequeue(out var message))
                {
                    _batch.Add(message);
                }

                if (diag)
                {
                    var sendStart = Stopwatch.GetTimestamp();
                    _sendService.Send(_batch);
                    var sendMs = (Stopwatch.GetTimestamp() - sendStart) * toMs;

                    _sendMs.Add(sendMs);
                    _batchSize.Add(_batch.Count);
                    _diagMessages += _batch.Count;

                    if (sendMs > SlowSendMs)
                    {
                        _logger.LogDebug("diag.send SLOW send {Ms:N2}ms for {Count} messages", sendMs, _batch.Count);
                    }
                }
                else
                {
                    _sendService.Send(_batch);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send {Count} queued OSC messages", _batch.Count);
            }
        }
    }
}
