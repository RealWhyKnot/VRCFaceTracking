using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public sealed class EyeLidMonitor(ILogger logger)
{
    private const double HeldReportMs = 250;
    private const double ClosedReportMs = 400;
    private const float ClosedBelow = 0.2f;

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

    public void Observe(long ticks, float rawLeft, float rawRight, float outLeft, float outRight)
    {
        var atBounds = IsBound(rawLeft) && IsBound(rawRight);
        var repeat = _hasLast && !atBounds
            && BitConverter.SingleToInt32Bits(rawLeft) == BitConverter.SingleToInt32Bits(_lastLeft)
            && BitConverter.SingleToInt32Bits(rawRight) == BitConverter.SingleToInt32Bits(_lastRight);

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
            var heldMs = Ms(ticks - _heldSince);
            if (heldMs >= HeldReportMs)
            {
                logger.LogDebug("diag.eye held {Ms:N0}ms at L={Left:0.000} R={Right:0.000} over {Updates} updates",
                    heldMs, _lastLeft, _lastRight, _heldUpdates);
            }
        }

        if (Math.Max(outLeft, outRight) < ClosedBelow)
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
            var closedMs = Ms(ticks - _closedSince);
            if (closedMs >= ClosedReportMs)
            {
                logger.LogDebug("diag.eye closed {Ms:N0}ms | raw L {MinLeft:0.00}-{MaxLeft:0.00} R {MinRight:0.00}-{MaxRight:0.00} | held {Held}/{Updates} updates",
                    closedMs, _minLeft, _maxLeft, _minRight, _maxRight, _closedHeldUpdates, _closedUpdates);
            }
        }

        _hasLast = true;
        _lastLeft = rawLeft;
        _lastRight = rawRight;
        _lastTicks = ticks;
    }

    private static bool IsBound(float value) => value == 0f || value == 1f;

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
