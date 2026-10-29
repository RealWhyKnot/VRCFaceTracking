using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Core.Tests;

public sealed partial class FileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vrcft-tests-" + Guid.NewGuid().ToString("N"));

    public FileLoggerTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void WritesHeaderAndGatesByLevel()
    {
        var gate = new LogLevelGate();
        using var provider = new FileLoggerProvider(_dir, "test.log", "HEADER\n", gate, TimeProvider.System);
        ILogger logger = provider.CreateLogger("Cat");

        LogDropped(logger);
        LogKept(logger, 42);
        gate.Set(verbose: true);
        LogNowVisible(logger);
        LogFailed(logger, new InvalidOperationException("boom"));
        provider.Flush();

        string text = ReadShared(provider.FilePath!);
        Assert.StartsWith("HEADER", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", text, StringComparison.Ordinal);
        Assert.Contains("[WRN] [Cat] kept 42", text, StringComparison.Ordinal);
        Assert.Contains("[DBG] [Cat] now visible", text, StringComparison.Ordinal);
        Assert.Contains("[ERR] [Cat] failed", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PassthroughCategoryIsNotReformatted()
    {
        string line = FileLogger.FormatLine(FileLogger.PassthroughCategory, LogLevel.Information, "[INF] [Child] hi", exception: null, new DateTime(2026, 1, 1, 10, 0, 0));
        Assert.Equal("10:00:00.000 [INF] [Child] hi" + Environment.NewLine, line);
    }

    [Fact]
    public void SecondProcessFallsBackToPidSuffix()
    {
        var gate = new LogLevelGate();
        using var first = new FileLoggerProvider(_dir, "same.log", "", gate, TimeProvider.System);
        using var second = new FileLoggerProvider(_dir, "same.log", "", gate, TimeProvider.System);
        Assert.True(second.IsOpen);
        Assert.NotEqual(first.FilePath, second.FilePath, StringComparer.Ordinal);
        Assert.EndsWith($"same_{Environment.ProcessId}.log", second.FilePath, StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "dropped")]
    private static partial void LogDropped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "kept {Value}")]
    private static partial void LogKept(ILogger logger, int value);

    [LoggerMessage(Level = LogLevel.Debug, Message = "now visible")]
    private static partial void LogNowVisible(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
