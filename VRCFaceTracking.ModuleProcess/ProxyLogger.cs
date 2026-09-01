using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.ModuleProcess;

public delegate void OnLog(LogLevel level, string msg);

public class ProxyLogger : ILogger
{
    private readonly string _categoryName;
    private readonly LogLevelGate _gate;
    public static OnLog OnLog;

    public ProxyLogger(string categoryName, LogLevelGate gate)
    {
        _categoryName = categoryName;
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
        if (!IsEnabled(logLevel) || OnLog == null)
        {
            return;
        }

        var message = exception == null
            ? formatter(state, exception)
            : $"{formatter(state, exception)}{Environment.NewLine}{exception}";
        OnLog(logLevel, $"[{FileLogger.Abbreviate(logLevel)}] [{_categoryName}] {message}");
    }
}
