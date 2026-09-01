using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Helpers;

namespace VRCFaceTracking.Services;

public struct LogLine
{
    public string Message;
    public LogLevel Level;

    public LogLine(string message, LogLevel level)
    {
        Message = message;
        Level = level;
    }

    public override string ToString() => Message;
}

public class OutputPageLogger : ILogger
{
    public const int MaxLines = 2000;

    private readonly string _categoryName;
    private readonly LogLevelGate _gate;
    public static readonly BoundedObservableCollection<LogLine> FilteredLogs = new(MaxLines);
    public static readonly BoundedObservableCollection<LogLine> AllLogs = new(MaxLines);
    private static DispatcherQueue? _dispatcher;

    public OutputPageLogger(string categoryName, DispatcherQueue? queue, LogLevelGate gate)
    {
        _categoryName = categoryName;
        _dispatcher = queue;
        _gate = gate;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => _gate.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var line = new LogLine(FileLogger.FormatLine(_categoryName, logLevel, formatter(state, exception), exception, DateTime.Now).TrimEnd(), logLevel);
        _dispatcher?.TryEnqueue(() =>
        {
            AllLogs.Add(line);
            if (logLevel >= LogLevel.Information)
            {
                FilteredLogs.Add(line);
            }
        });
    }
}
