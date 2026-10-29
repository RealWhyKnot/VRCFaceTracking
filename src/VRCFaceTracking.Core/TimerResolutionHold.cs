using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core;

public sealed class TimerResolutionHold(ILogger logger, Func<bool> raise, Action release)
{
    public const int IdleSeconds = 10;

    private bool _raised;
    private long _lastActive;

    public TimerResolutionHold(ILogger logger)
        : this(logger, () => OperatingSystem.IsWindows() && Utils.TimeBeginPeriod(1) == 0, () => Utils.TimeEndPeriod(1))
    {
    }

    public void Observe(long ticks, bool active)
    {
        if (active)
        {
            _lastActive = ticks;
            if (!_raised && raise())
            {
                _raised = true;
                logger.LogInformation("Raised timer resolution to 1 ms");
            }
        }
        else if (_raised && ticks - _lastActive >= IdleSeconds * Stopwatch.Frequency)
        {
            Release();
        }
    }

    public void Release()
    {
        if (!_raised)
        {
            return;
        }

        release();
        _raised = false;
        logger.LogInformation("Released 1 ms timer resolution");
    }
}
