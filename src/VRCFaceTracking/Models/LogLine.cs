using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Models;

internal sealed record LogLine(string Message, LogLevel Level)
{
    public override string ToString() => Message;
}
