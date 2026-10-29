using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core;

public sealed partial class TimerResolutionHold
{
    public const int IdleSeconds = 10;

    private bool _raised;
    private long _lastActive;

    private readonly ILogger _logger;
    private readonly Func<bool> _raise;
    private readonly Action _release;

    public TimerResolutionHold(ILogger logger, Func<bool> raise, Action release)
    {
        _logger = logger;
        _raise = raise;
        _release = release;
    }

    public TimerResolutionHold(ILogger logger)
        : this(logger, () => OperatingSystem.IsWindows() && Utils.TimeBeginPeriod(1) == 0, () => _ = Utils.TimeEndPeriod(1))
    {
    }

    public void Observe(long ticks, bool active)
    {
        if (active)
        {
            _lastActive = ticks;
            if (!_raised && _raise())
            {
                _raised = true;
                LogRaisedTimerResolution1Ms(_logger);
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

        _release();
        _raised = false;
        LogReleased1MsTimerResolution(_logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Raised timer resolution to 1 ms")]
    private static partial void LogRaisedTimerResolution1Ms(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released 1 ms timer resolution")]
    private static partial void LogReleased1MsTimerResolution(ILogger logger);
}
