using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public sealed class LogLevelGate
{
    public LogLevel Minimum { get; private set; } = LogLevel.Warning;

    public bool Verbose => Minimum <= LogLevel.Debug;

    public event EventHandler? VerboseChanged;

    public void Set(bool verbose)
    {
        LogLevel minimum = verbose ? LogLevel.Debug : LogLevel.Warning;
        if (minimum == Minimum)
        {
            return;
        }

        Minimum = minimum;
        VerboseChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= Minimum;
}
