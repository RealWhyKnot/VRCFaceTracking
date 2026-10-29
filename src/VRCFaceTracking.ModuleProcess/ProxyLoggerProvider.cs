using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.ModuleProcess;

internal sealed class ProxyLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, ProxyLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly LogLevelGate _gate;

    public ProxyLoggerProvider(LogLevelGate gate)
    {
        _gate = gate;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new ProxyLogger(name, _gate));

    public void Dispose() => _loggers.Clear();
}
