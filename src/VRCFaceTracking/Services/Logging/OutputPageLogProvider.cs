using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Services.Logging;

public sealed class OutputPageLogProvider(LogLevelGate gate) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OutputPageLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new OutputPageLogger(name, gate));

    public void Dispose() => _loggers.Clear();
}
