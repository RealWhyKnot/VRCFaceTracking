using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Perf.Tests;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName.Replace("\0", "", StringComparison.Ordinal));

    public string Tail(int count = 40) => string.Join(Environment.NewLine, _lines.TakeLast(count));

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _owner;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _owner._lines.Enqueue($"{logLevel} {_category}: {formatter(state, exception)} {exception?.Message}".TrimEnd());
            }
        }
    }
}
