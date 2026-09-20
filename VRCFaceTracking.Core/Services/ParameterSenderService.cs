using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
    private const double StallTickGapMs = 50;
    private const double SlowSendMs = 5;
    private const double StaleDataMs = 100;

    private readonly DiagStat _tickGap = new();
    private readonly DiagStat _sendMs = new();
    private readonly DiagStat _batchSize = new();
    private readonly DiagStat _dataAgeMs = new();
    private long _diagStalls;
    private long _diagMessages;
    private bool _staleReported;

    private static readonly ConcurrentQueue<OscMessage> SendQueue = new();
    private readonly List<OscMessage> _batch = new();

    private readonly OscSendService _sendService;
    private readonly ILogger<ParameterSenderService> _logger;
    private readonly UnifiedTrackingMutator _mutator; // We don't use this but we do want DI to run its constructor

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
    }

    private void ReportDiagnostics()
    {
        var (gapP50, gapP99) = _tickGap.Percentiles();
        var (sendP50, sendP99) = _sendMs.Percentiles();
        var (batchP50, _) = _batchSize.Percentiles();
        var (ageP50, ageP99) = _dataAgeMs.Percentiles();

        _logger.LogDebug(
            "diag.send ticks={Ticks} gap p50={GapP50:N1} p99={GapP99:N1} max={GapMax:N1}ms | batch p50={BatchP50:N0} max={BatchMax:N0} msgs={Msgs} | send p50={SendP50:N2} p99={SendP99:N2} max={SendMax:N2}ms | dataAge p50={AgeP50:N1} p99={AgeP99:N1} max={AgeMax:N1}ms | queue={Queue} stalls={Stalls}",
            _tickGap.Count, gapP50, gapP99, _tickGap.Max,
            batchP50, _batchSize.Max, _diagMessages,
            sendP50, sendP99, _sendMs.Max,
            ageP50, ageP99, _dataAgeMs.Max,
            SendQueue.Count, _diagStalls);

        _tickGap.Reset();
        _sendMs.Reset();
        _batchSize.Reset();
        _dataAgeMs.Reset();
        _diagMessages = 0;
    }

    public static void Enqueue(OscMessage message) => SendQueue.Enqueue(message);
    public static void Clear() => SendQueue.Clear();

    protected async override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        var lastDataVersion = -1;
        var toMs = 1000.0 / Stopwatch.Frequency;
        var lastTick = Stopwatch.GetTimestamp();
        var lastReport = lastTick;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }

                var tickNow = Stopwatch.GetTimestamp();
                var gapMs = (tickNow - lastTick) * toMs;
                lastTick = tickNow;

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _tickGap.Add(gapMs);

                    var updateTicks = UnifiedTracking.LastDataUpdateTicks;
                    if (updateTicks != 0)
                    {
                        _dataAgeMs.Add((tickNow - updateTicks) * toMs);
                    }

                    if (gapMs > StallTickGapMs)
                    {
                        _diagStalls++;
                        _logger.LogDebug("diag.send STALL tick gap {Gap:N1}ms (expected {Expected}ms), queue {Queue}",
                            gapMs, TickIntervalMs, SendQueue.Count);
                    }

                    if (updateTicks != 0)
                    {
                        var ageMs = (tickNow - updateTicks) * toMs;
                        if (ageMs > StaleDataMs && !_staleReported)
                        {
                            _staleReported = true;
                            _logger.LogDebug("diag.send tracking data is {Age:N0}ms old, no module update since", ageMs);
                        }
                        else if (ageMs <= StaleDataMs && _staleReported)
                        {
                            _staleReported = false;
                            _logger.LogDebug("diag.send module updates resumed");
                        }
                    }

                    if ((tickNow - lastReport) * toMs >= 1000)
                    {
                        lastReport = tickNow;
                        ReportDiagnostics();
                    }
                }

                var dataVersion = UnifiedTracking.DataVersion;
                if (dataVersion != lastDataVersion)
                {
                    lastDataVersion = dataVersion;
                    UnifiedTracking.UpdateData();
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

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var sendStart = Stopwatch.GetTimestamp();
                    await _sendService.Send(_batch, cancellationToken);
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
                    await _sendService.Send(_batch, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send {Count} queued OSC messages", _batch.Count);
            }
        }
    }
}