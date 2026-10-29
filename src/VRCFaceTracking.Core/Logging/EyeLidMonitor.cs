using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public sealed partial class EyeLidMonitor
{
    private const double HeldReportMs = 250;
    private const double ClosedReportMs = 400;
    private const float ClosedBelow = 0.2f;

    private readonly ILogger _logger;

    private bool _hasLast;
    private float _lastLeft;
    private float _lastRight;
    private long _lastTicks;

    private bool _held;
    private long _heldSince;
    private int _heldUpdates;

    private bool _closed;
    private long _closedSince;
    private int _closedUpdates;
    private int _closedHeldUpdates;
    private float _minLeft, _maxLeft, _minRight, _maxRight;

    public EyeLidMonitor(ILogger logger)
    {
        _logger = logger;
    }

    public void Observe(long ticks, float rawLeft, float rawRight, float outLeft, float outRight)
    {
        bool repeat = IsRepeat(rawLeft, rawRight);
        TrackHeld(ticks, repeat);
        TrackClosed(ticks, rawLeft, rawRight, Math.Max(outLeft, outRight) < ClosedBelow, repeat);

        _hasLast = true;
        _lastLeft = rawLeft;
        _lastRight = rawRight;
        _lastTicks = ticks;
    }

    private bool IsRepeat(float rawLeft, float rawRight) =>
        _hasLast && !(IsBound(rawLeft) && IsBound(rawRight))
        && BitConverter.SingleToInt32Bits(rawLeft) == BitConverter.SingleToInt32Bits(_lastLeft)
        && BitConverter.SingleToInt32Bits(rawRight) == BitConverter.SingleToInt32Bits(_lastRight);

    private void TrackHeld(long ticks, bool repeat)
    {
        if (repeat)
        {
            if (!_held)
            {
                _held = true;
                _heldSince = _lastTicks;
                _heldUpdates = 1;
            }
            _heldUpdates++;
        }
        else if (_held)
        {
            _held = false;
            double heldMs = Ms(ticks - _heldSince);
            if (heldMs >= HeldReportMs)
            {
                LogEyeHeld(_logger, heldMs, _lastLeft, _lastRight, _heldUpdates);
            }
        }
    }

    private void TrackClosed(long ticks, float rawLeft, float rawRight, bool closed, bool repeat)
    {
        if (closed)
        {
            if (!_closed)
            {
                _closed = true;
                _closedSince = ticks;
                _closedUpdates = 0;
                _closedHeldUpdates = 0;
                _minLeft = _maxLeft = rawLeft;
                _minRight = _maxRight = rawRight;
            }
            _closedUpdates++;
            if (repeat)
            {
                _closedHeldUpdates++;
            }
            _minLeft = Math.Min(_minLeft, rawLeft);
            _maxLeft = Math.Max(_maxLeft, rawLeft);
            _minRight = Math.Min(_minRight, rawRight);
            _maxRight = Math.Max(_maxRight, rawRight);
        }
        else if (_closed)
        {
            _closed = false;
            double closedMs = Ms(ticks - _closedSince);
            if (closedMs >= ClosedReportMs)
            {
                LogEyeClosed(_logger, closedMs, _minLeft, _maxLeft, _minRight, _maxRight, _closedHeldUpdates, _closedUpdates);
            }
        }
    }

    private static bool IsBound(float value) => value is 0f or 1f;

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.eye held {Ms:N0}ms at L={Left:0.000} R={Right:0.000} over {Updates} updates")]
    private static partial void LogEyeHeld(ILogger logger, double ms, float left, float right, int updates);

    [LoggerMessage(Level = LogLevel.Debug, Message = "diag.eye closed {Ms:N0}ms | raw L {MinLeft:0.00}-{MaxLeft:0.00} R {MinRight:0.00}-{MaxRight:0.00} | held {Held}/{Updates} updates")]
    private static partial void LogEyeClosed(ILogger logger, double ms, float minLeft, float maxLeft, float minRight, float maxRight, int held, int updates);
}
