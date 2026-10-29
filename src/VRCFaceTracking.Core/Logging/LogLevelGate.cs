using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public sealed class LogLevelGate
{
    public LogLevel Minimum { get; private set; } = LogLevel.Warning;

    public bool Verbose => Minimum <= LogLevel.Debug;

    public event Action<bool>? VerboseChanged;

    public void Set(bool verbose)
    {
        var minimum = verbose ? LogLevel.Debug : LogLevel.Warning;
        if (minimum == Minimum)
        {
            return;
        }

        Minimum = minimum;
        VerboseChanged?.Invoke(verbose);
    }

    public bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= Minimum;
}
