using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Services.Logging;

namespace VRCFaceTracking.UiTests;

public class LoggingTests
{
    [AvaloniaFact]
    public void OutputPageQueuesDebugOnlyWhenVerbose()
    {
        var gate = new LogLevelGate();
        var logger = new OutputPageLogger(nameof(LoggingTests), gate);
        var quiet = Guid.NewGuid().ToString("N");
        var info = Guid.NewGuid().ToString("N");
        var verbose = Guid.NewGuid().ToString("N");

        logger.LogDebug(quiet);
        logger.LogInformation(info);
        gate.Set(true);
        logger.LogDebug(verbose);

        var queued = OutputPageLogger.Pending.Select(line => line.Message).ToList();
        Assert.DoesNotContain(queued, message => message.Contains(quiet));
        Assert.Contains(queued, message => message.Contains(info));
        Assert.Contains(queued, message => message.Contains(verbose));
    }
}
