using System.Reflection;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Tests;

public class LogLevelGateTests
{
    [Fact]
    public void DefaultsToWarning()
    {
        var gate = new LogLevelGate();
        Assert.False(gate.Verbose);
        Assert.False(gate.IsEnabled(LogLevel.Information));
        Assert.True(gate.IsEnabled(LogLevel.Warning));
        Assert.False(gate.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void VerboseLetsDebugThroughAndRaisesOnce()
    {
        var gate = new LogLevelGate();
        var raised = new List<bool>();
        gate.VerboseChanged += raised.Add;
        gate.Set(true);
        gate.Set(true);
        gate.Set(false);
        Assert.Equal(new[] { true, false }, raised);
        Assert.False(gate.IsEnabled(LogLevel.Debug));
        gate.Set(true);
        Assert.True(gate.IsEnabled(LogLevel.Debug));
        Assert.False(gate.IsEnabled(LogLevel.Trace));
    }
}

public class BuildInfoTests
{
    [Theory]
    [InlineData("dev", BuildChannel.Dev)]
    [InlineData("BETA", BuildChannel.Beta)]
    [InlineData("Release", BuildChannel.Release)]
    [InlineData("nightly", BuildChannel.Dev)]
    [InlineData("", BuildChannel.Dev)]
    [InlineData(null, BuildChannel.Dev)]
    public void ParseChannel_FallsBackToDev(string? value, BuildChannel expected)
    {
        Assert.Equal(expected, BuildInfo.ParseChannel(value));
    }

    [Fact]
    public void ChannelComesFromAssemblyMetadataAndDrivesVerboseForcing()
    {
        var metadata = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == BuildInfo.ChannelMetadataKey).Value;
        Assert.Equal(BuildInfo.ParseChannel(metadata), BuildInfo.Channel);
        Assert.Equal(BuildInfo.Channel != BuildChannel.Release, BuildInfo.VerboseForced);
    }

    [Fact]
    public void VersionStringCarriesTheBuildStamp()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+(-[A-Za-z0-9]+)?$", BuildInfo.VersionString);
        Assert.StartsWith($"{BuildInfo.Version.Major}.{BuildInfo.Version.Minor}.{BuildInfo.Version.Build}.{BuildInfo.Version.Revision}", BuildInfo.VersionString);
    }

    [Fact]
    public void HeaderBlockNamesProcessVersionAndChannel()
    {
        var header = BuildInfo.HeaderBlock("Tests");
        Assert.Contains("Tests", header);
        Assert.Contains(BuildInfo.VersionString, header);
        Assert.Contains($"({BuildInfo.ChannelName})", header);
        Assert.Contains($"PID {Environment.ProcessId}", header);
    }
}

public class LogFileNamesTests
{
    [Fact]
    public void MainAndModuleNamesFollowPattern()
    {
        var ts = new DateTime(2026, 9, 1, 13, 5, 9);
        Assert.Equal("vrcft_2026-09-01_13-05-09.log", LogFileNames.Main(ts));
        Assert.Equal("vrcft_module_VirtualDesktop.FaceTracking_2026-09-01_13-05-09.log", LogFileNames.Module("VirtualDesktop.FaceTracking", ts));
    }

    [Theory]
    [InlineData("a b:c", "a_b_c")]
    [InlineData("///", "module")]
    [InlineData("__x__", "x")]
    public void SanitizeStripsInvalidCharacters(string input, string expected)
    {
        Assert.Equal(expected, LogFileNames.Sanitize(input));
    }
}

public class LogRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vrcft-tests-" + Guid.NewGuid().ToString("N"));

    public LogRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name, DateTime created)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, name);
        File.SetCreationTimeUtc(path, created);
        return path;
    }

    [Fact]
    public void KeepsNewestFilesOnly()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 25; i++)
        {
            Touch(LogFileNames.Main(start.AddMinutes(i)), start.AddMinutes(i));
        }

        var deleted = LogRetention.Prune(_dir, LogFileNames.MainPattern, 20);

        Assert.Equal(5, deleted);
        var remaining = Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(20, remaining.Length);
        Assert.Equal(LogFileNames.Main(start.AddMinutes(5)), remaining[0]);
        Assert.Equal(LogFileNames.Main(start.AddMinutes(24)), remaining[^1]);
    }

    [Fact]
    public void ModuleFilesDoNotEvictMainFiles()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Touch(LogFileNames.Main(start), start);
        for (var i = 1; i <= 5; i++)
        {
            Touch(LogFileNames.Module("m", start.AddMinutes(i)), start.AddMinutes(i));
        }

        LogRetention.Prune(_dir, LogFileNames.MainPattern, 2);
        LogRetention.Prune(_dir, LogFileNames.ModulePattern, 2);

        var remaining = Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray();
        Assert.Contains(LogFileNames.Main(start), remaining);
        Assert.Equal(2, remaining.Count(f => f.StartsWith("vrcft_module_")));
    }

    [Fact]
    public void LockedFileIsSkipped()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var locked = Touch(LogFileNames.Main(start), start);
        Touch(LogFileNames.Main(start.AddMinutes(1)), start.AddMinutes(1));
        Touch(LogFileNames.Main(start.AddMinutes(2)), start.AddMinutes(2));

        using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var deleted = LogRetention.Prune(_dir, LogFileNames.MainPattern, 1);

        Assert.Equal(1, deleted);
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void MissingDirectoryOrZeroKeepIsNoop()
    {
        Assert.Equal(0, LogRetention.Prune(Path.Combine(_dir, "missing"), "*", 5));
        Touch("vrcft_2026-01-01_00-00-00.log", DateTime.UtcNow);
        Assert.Equal(0, LogRetention.Prune(_dir, LogFileNames.MainPattern, 0));
    }
}

public class FileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vrcft-tests-" + Guid.NewGuid().ToString("N"));

    public FileLoggerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

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
        using var provider = new FileLoggerProvider(_dir, "test.log", "HEADER\n", gate);
        var logger = provider.CreateLogger("Cat");

        logger.LogInformation("dropped");
        logger.LogWarning("kept {Value}", 42);
        gate.Set(true);
        logger.LogDebug("now visible");
        logger.LogError(new InvalidOperationException("boom"), "failed");
        provider.Flush();

        var text = ReadShared(provider.FilePath!);
        Assert.StartsWith("HEADER", text);
        Assert.DoesNotContain("dropped", text);
        Assert.Contains("[WRN] [Cat] kept 42", text);
        Assert.Contains("[DBG] [Cat] now visible", text);
        Assert.Contains("[ERR] [Cat] failed", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }

    [Fact]
    public void PassthroughCategoryIsNotReformatted()
    {
        var line = FileLogger.FormatLine(FileLogger.PassthroughCategory, LogLevel.Information, "[INF] [Child] hi", null, new DateTime(2026, 1, 1, 10, 0, 0));
        Assert.Equal("10:00:00.000 [INF] [Child] hi" + Environment.NewLine, line);
    }

    [Fact]
    public void SecondProcessFallsBackToPidSuffix()
    {
        var gate = new LogLevelGate();
        using var first = new FileLoggerProvider(_dir, "same.log", "", gate);
        using var second = new FileLoggerProvider(_dir, "same.log", "", gate);
        Assert.True(second.IsOpen);
        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.EndsWith($"same_{Environment.ProcessId}.log", second.FilePath);
    }
}

public class ModuleProcessExitCodesTests
{
    [Theory]
    [InlineData(ModuleProcessExitCodes.OK, "clean exit")]
    [InlineData(ModuleProcessExitCodes.EXCEPTION_CRASH, "unhandled exception")]
    [InlineData(ModuleProcessExitCodes.MODULE_LOAD_FAILED, "module assembly failed to load")]
    [InlineData(ModuleProcessExitCodes.PARENT_EXITED, "host process exited")]
    [InlineData(-1, "killed (exit code -1)")]
    [InlineData(1, "killed (exit code 1)")]
    [InlineData(42, "exit code 42")]
    public void DescribeKnownCodes(int code, string expected)
    {
        Assert.Equal(expected, ModuleProcessExitCodes.Describe(code));
    }

    [Fact]
    public void DescribeNativeCodes()
    {
        Assert.Equal("access violation (0xC0000005)", ModuleProcessExitCodes.Describe(unchecked((int)0xC0000005)));
        Assert.Equal("unhandled .NET exception (0xE0434352)", ModuleProcessExitCodes.Describe(unchecked((int)0xE0434352)));
        Assert.Equal("native exit code 0xC0000409", ModuleProcessExitCodes.Describe(unchecked((int)0xC0000409)));
    }
}

public class EventSetVerbosePacketTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTripsThroughDecoder(bool verbose)
    {
        var bytes = new EventSetVerbosePacket { Verbose = verbose }.GetBytes();
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out var decoded));
        var packet = Assert.IsType<EventSetVerbosePacket>(decoded);
        Assert.Equal(verbose, packet.Verbose);
    }
}
