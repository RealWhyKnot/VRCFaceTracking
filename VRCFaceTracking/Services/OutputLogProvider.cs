using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Services;

public sealed class OutputLogProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OutputPageLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherQueue _dispatcher;
    private readonly LogLevelGate _gate;

    public OutputLogProvider(DispatcherQueue dispatcher, LogLevelGate gate)
    {
        _dispatcher = dispatcher;
        _gate = gate;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new OutputPageLogger(name, _dispatcher, _gate));

    public void Dispose() => _loggers.Clear();
}
