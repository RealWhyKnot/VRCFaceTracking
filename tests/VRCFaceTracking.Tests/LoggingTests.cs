using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Services.Logging;

namespace VRCFaceTracking.Tests;

public sealed partial class LoggingTests
{
    [AvaloniaFact]
    public void OutputPageQueuesDebugOnlyWhenVerbose()
    {
        var gate = new LogLevelGate();
        var logger = new OutputPageLogger(nameof(LoggingTests), gate);
        string quiet = Guid.NewGuid().ToString("N");
        string info = Guid.NewGuid().ToString("N");
        string verbose = Guid.NewGuid().ToString("N");

        LogLine(logger, LogLevel.Debug, quiet);
        LogLine(logger, LogLevel.Information, info);
        gate.Set(verbose: true);
        LogLine(logger, LogLevel.Debug, verbose);

        var queued = OutputPageLogger.Pending.Select(line => line.Message).ToList();
        Assert.DoesNotContain(queued, message => message.Contains(quiet, StringComparison.Ordinal));
        Assert.Contains(queued, message => message.Contains(info, StringComparison.Ordinal));
        Assert.Contains(queued, message => message.Contains(verbose, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void HostDebugFollowsVerbose()
    {
        LogLevelGate gate = App.GetService<LogLevelGate>();
        ILogger logger = App.GetService<ILoggerFactory>().CreateLogger(nameof(LoggingTests));
        try
        {
            gate.Set(verbose: false);
            Assert.False(logger.IsEnabled(LogLevel.Debug));
            gate.Set(verbose: true);
            Assert.True(logger.IsEnabled(LogLevel.Debug));
        }
        finally
        {
            gate.Set(verbose: false);
        }
    }

    [AvaloniaFact]
    public void FileLogWritesToTheTestLogDirectory()
    {
        string? path = App.GetService<FileLoggerProvider>().FilePath;
        Assert.Equal(Environment.GetEnvironmentVariable(LogDirectories.EnvironmentVariable), Path.GetDirectoryName(path));
    }

    [LoggerMessage(Message = "{Message}")]
    private static partial void LogLine(ILogger logger, LogLevel level, string message);
}
