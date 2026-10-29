using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Services.Logging;

internal sealed class OutputPageLogProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OutputPageLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly LogLevelGate _gate;

    public OutputPageLogProvider(LogLevelGate gate)
    {
        _gate = gate;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new OutputPageLogger(name, _gate));

    public void Dispose() => _loggers.Clear();
}
