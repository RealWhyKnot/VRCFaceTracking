using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.ModuleProcess;

public class ProxyLoggerProvider(LogLevelGate gate) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, ProxyLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new ProxyLogger(name, gate));

    public void Dispose() => _loggers.Clear();
}
