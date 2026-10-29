using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Services;

public sealed partial class ParameterSenderService : BackgroundService
{
    private const double MinDataIntervalMs = 5;
    private const double RefreshIntervalMs = 25;
    private const double IdleRefreshIntervalMs = 250;
    private const double IdleAfterMs = 1000;
    private const double StallLateMs = 40;
    private const double SlowSendMs = 5;
    private const double StaleDataMs = 250;
    private static readonly double ToMs = 1000.0 / Stopwatch.Frequency;

    private readonly DiagStat _tickGap = new();
    private readonly DiagStat _sendMs = new();
    private readonly DiagStat _batchSize = new();
    private readonly DiagStat _sampleAgeMs = new();
    private long _diagStalls;
    private int _wakes;
    private long _diagMessages;
    private long _staleSinceTicks;
    private TimeSpan _lastGcPause;
    private TimeSpan _lastReportGcPause;
    private int _lastGen0, _lastGen1, _lastGen2;

    private static readonly ConcurrentQueue<OscMessage> SendQueue = new();
    private readonly List<OscMessage> _batch = [];

    private readonly OscSendService _sendService;
    private readonly ILogger<ParameterSenderService> _logger;
    private readonly UnifiedTrackingMutator _mutator;
    private readonly EyeLidMonitor _eyeLids;
    private readonly TimerResolutionHold _timerResolution;
    private int _lastDataVersion;
    private long _lastTick;
    private long _lastReport;
    private long _lastUpdate;
    private long _lastDataUpdate;

    public static bool AllParametersRelevantStatic
    {
        get; set;
    }
    public static bool AllParametersRelevant
    {
        get => AllParametersRelevantStatic;
        set
        {
            if (AllParametersRelevantStatic == value)
            {
                return;
            }

            AllParametersRelevantStatic = value;
            SendQueue.Clear();
            foreach (Parameter parameter in TrackingOutput.AllParameters)
            {
                parameter.ResetParam([]);
            }

            UnifiedTracking.MarkDataUpdated();
        }
    }

    public ParameterSenderService(OscSendService sendService, UnifiedTrackingMutator mutator, ILogger<ParameterSenderService> logger)
        : this(sendService, mutator, logger, new TimerResolutionHold(logger))
    {
    }

    internal ParameterSenderService(OscSendService sendService, UnifiedTrackingMutator mutator, ILogger<ParameterSenderService> logger, TimerResolutionHold timerResolution)
    {
        _sendService = sendService;
        _logger = logger;
        _mutator = mutator;
        _eyeLids = new EyeLidMonitor(logger);
        _timerResolution = timerResolution;
        _lastDataVersion = UnifiedTracking.DataVersion;
    }

    private void ReportDiagnostics()
    {
        (double gapP50, double gapP99) = _tickGap.Percentiles();
        (double sendP50, double sendP99) = _sendMs.Percentiles();
        (double batchP50, double _) = _batchSize.Percentiles();
        (double ageP50, double ageP99) = _sampleAgeMs.Percentiles();

        TimeSpan gcPause = GC.GetTotalPauseDuration();
        LogDiagSendTicksGapP50(_logger, _tickGap.Count, gapP50, gapP99, _tickGap.Max, batchP50, _batchSize.Max, _diagMessages, sendP50, sendP99, _sendMs.Max, ageP50, ageP99, _sampleAgeMs.Max, SendQueue.Count, _diagStalls, (gcPause - _lastReportGcPause).TotalMilliseconds);
        _lastReportGcPause = gcPause;

        _tickGap.Reset();
        _sendMs.Reset();
        _batchSize.Reset();
        _sampleAgeMs.Reset();
        _diagMessages = 0;
    }

    public static void Enqueue(OscMessage message) => SendQueue.Enqueue(message);
    public static void Clear() => SendQueue.Clear();

    internal int Wakes => Volatile.Read(ref _wakes);

    internal int SendQueued()
    {
        _batch.Clear();
        while (SendQueue.TryDequeue(out OscMessage? message))
        {
            _batch.Add(message);
        }

        if (_batch.Count > 0)
        {
            _sendService.Send(_batch);
        }
        return _batch.Count;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                Run(stoppingToken);
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
        WaitHandle[] wakeHandles = [UnifiedTracking.DataArrived, cancellationToken.WaitHandle];
        long start = Stopwatch.GetTimestamp();
        _lastTick = start;
        _lastReport = start;
        _lastUpdate = start;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                int waitMs = NextWaitMs();
                WaitHandle.WaitAny(wakeHandles, waitMs);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                Tick(waitMs);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                LogFailedSendQueuedOscMessages(_logger, e, _batch.Count);
            }
        }

        _timerResolution.Release();
    }

    private int NextWaitMs()
    {
        long now = Stopwatch.GetTimestamp();
        double refreshMs = (now - _lastDataUpdate) * ToMs >= IdleAfterMs ? IdleRefreshIntervalMs : RefreshIntervalMs;
        return (int)Math.Ceiling(Math.Max(0, refreshMs - ((now - _lastUpdate) * ToMs)));
    }

    private void Tick(int waitMs)
    {
        Interlocked.Increment(ref _wakes);

        long tickNow = Stopwatch.GetTimestamp();
        double gapMs = (tickNow - _lastTick) * ToMs;
        _lastTick = tickNow;

        bool diag = _logger.IsEnabled(LogLevel.Debug);
        if (diag)
        {
            RecordTickDiagnostics(tickNow, gapMs, waitMs);
        }

        UpdateOutput(tickNow, diag);

        if (!SendQueue.IsEmpty)
        {
            SendBatch(diag);
        }
    }

    private void RecordTickDiagnostics(long tickNow, double gapMs, int waitMs)
    {
        _tickGap.Add(gapMs);

        TimeSpan gcPause = GC.GetTotalPauseDuration();
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        if (gapMs - waitMs > StallLateMs)
        {
            _diagStalls++;
            LogDiagSendStallTickGap(_logger, gapMs, waitMs, SendQueue.Count, (gcPause - _lastGcPause).TotalMilliseconds, gen0 - _lastGen0, gen1 - _lastGen1, gen2 - _lastGen2, ThreadPool.PendingWorkItemCount, ThreadPool.ThreadCount);
        }
        _lastGcPause = gcPause;
        _lastGen0 = gen0;
        _lastGen1 = gen1;
        _lastGen2 = gen2;

        TrackStaleData(tickNow);

        if ((tickNow - _lastReport) * ToMs >= 1000)
        {
            _lastReport = tickNow;
            ReportDiagnostics();
        }
    }

    private void TrackStaleData(long tickNow)
    {
        long updateTicks = UnifiedTracking.LastDataUpdateTicks;
        if (updateTicks == 0)
        {
            return;
        }

        double ageMs = (tickNow - updateTicks) * ToMs;
        if (ageMs > StaleDataMs && _staleSinceTicks == 0)
        {
            _staleSinceTicks = updateTicks;
            LogDiagSendNoNewTracking(_logger, ageMs);
        }
        else if (ageMs <= StaleDataMs && _staleSinceTicks != 0)
        {
            LogDiagSendTrackingDataResumed(_logger, (updateTicks - _staleSinceTicks) * ToMs);
            _staleSinceTicks = 0;
        }
    }

    private void UpdateOutput(long tickNow, bool diag)
    {
        bool dataChanged = UnifiedTracking.DataVersion != _lastDataVersion;
        _timerResolution.Observe(tickNow, dataChanged);
        if (!dataChanged)
        {
            if ((tickNow - _lastUpdate) * ToMs >= RefreshIntervalMs)
            {
                TrackingOutput.UpdateData();
                _lastUpdate = Stopwatch.GetTimestamp();
            }
            return;
        }

        double sinceDataMs = (Stopwatch.GetTimestamp() - _lastDataUpdate) * ToMs;
        if (sinceDataMs < MinDataIntervalMs)
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(MinDataIntervalMs - sinceDataMs));
        }

        _lastDataVersion = UnifiedTracking.DataVersion;
        TrackingOutput.UpdateData();
        _lastDataUpdate = _lastUpdate = Stopwatch.GetTimestamp();

        if (diag)
        {
            _sampleAgeMs.Add((_lastDataUpdate - UnifiedTracking.LastSampleTicks) * ToMs);
            (float rawLeft, float rawRight, float outLeft, float outRight) = _mutator.LastEyeOpenness;
            _eyeLids.Observe(_lastDataUpdate, rawLeft, rawRight, outLeft, outRight);
        }
    }

    private void SendBatch(bool diag)
    {
        if (!diag)
        {
            SendQueued();
            return;
        }

        long sendStart = Stopwatch.GetTimestamp();
        int sent = SendQueued();
        double sendMs = (Stopwatch.GetTimestamp() - sendStart) * ToMs;

        _sendMs.Add(sendMs);
        _batchSize.Add(sent);
        _diagMessages += sent;

        if (sendMs > SlowSendMs)
        {
            LogDiagSendSlowSendMs(_logger, sendMs, sent);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.send ticks={Ticks} gap p50={GapP50:N1} p99={GapP99:N1} max={GapMax:N1}ms | batch p50={BatchP50:N0} max={BatchMax:N0} msgs={Msgs} | send p50={SendP50:N2} p99={SendP99:N2} max={SendMax:N2}ms | sampleAge p50={AgeP50:N1} p99={AgeP99:N1} max={AgeMax:N1}ms | queue={Queue} stalls={Stalls} gcPause={GcPause:N1}ms")]
    private static partial void LogDiagSendTicksGapP50(ILogger logger, int ticks, double gapP50, double gapP99, double gapMax, double batchP50, double batchMax, long msgs, double sendP50, double sendP99, double sendMax, double ageP50, double ageP99, double ageMax, int queue, long stalls, double gcPause);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.send STALL tick gap {Gap:N1}ms (expected {Expected}ms), queue {Queue} | gcPause {GcPause:N1}ms gcs {Gen0}/{Gen1}/{Gen2} | threadPool pending {Pending} threads {Threads}")]
    private static partial void LogDiagSendStallTickGap(ILogger logger, double gap, int expected, int queue, double gcPause, int gen0, int gen1, int gen2, long pending, int threads);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.send no new tracking data for {Age:N0}ms")]
    private static partial void LogDiagSendNoNewTracking(ILogger logger, double age);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.send tracking data resumed after {Gap:N0}ms")]
    private static partial void LogDiagSendTrackingDataResumed(ILogger logger, double gap);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.send SLOW send {Ms:N2}ms for {Count} messages")]
    private static partial void LogDiagSendSlowSendMs(ILogger logger, double ms, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send {Count} queued OSC messages")]
    private static partial void LogFailedSendQueuedOscMessages(ILogger logger, Exception exception, int count);
}
