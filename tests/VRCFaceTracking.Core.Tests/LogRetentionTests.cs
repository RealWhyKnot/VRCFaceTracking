using VRCFaceTracking.Core.Logging;

namespace VRCFaceTracking.Core.Tests;

public sealed class LogRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vrcft-tests-" + Guid.NewGuid().ToString("N"));

    public LogRetentionTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name, DateTime created)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, name);
        File.SetCreationTimeUtc(path, created);
        return path;
    }

    [Fact]
    public void KeepsNewestFilesOnly()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 25; i++)
        {
            Touch(LogFileNames.Main(start.AddMinutes(i)), start.AddMinutes(i));
        }

        int deleted = LogRetention.Prune(_dir, LogFileNames.MainPattern, 20);

        Assert.Equal(5, deleted);
        string?[] remaining = [.. Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal)];
        Assert.Equal(20, remaining.Length);
        Assert.Equal(LogFileNames.Main(start.AddMinutes(5)), remaining[0]);
        Assert.Equal(LogFileNames.Main(start.AddMinutes(24)), remaining[^1]);
    }

    [Fact]
    public void ModuleFilesDoNotEvictMainFiles()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Touch(LogFileNames.Main(start), start);
        for (int i = 1; i <= 5; i++)
        {
            Touch(LogFileNames.Module("m", start.AddMinutes(i)), start.AddMinutes(i));
        }

        LogRetention.Prune(_dir, LogFileNames.MainPattern, 2);
        LogRetention.Prune(_dir, LogFileNames.ModulePattern, 2);

        string[] remaining = [.. new DirectoryInfo(_dir).GetFiles().Select(f => f.Name)];
        Assert.Contains(LogFileNames.Main(start), remaining, StringComparer.Ordinal);
        Assert.Equal(2, remaining.Count(f => f.StartsWith("vrcft_module_", StringComparison.Ordinal)));
    }

    [Fact]
    public void LockedFileIsSkippedOnlyOnWindows()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string locked = Touch(LogFileNames.Main(start), start);
        Touch(LogFileNames.Main(start.AddMinutes(1)), start.AddMinutes(1));
        Touch(LogFileNames.Main(start.AddMinutes(2)), start.AddMinutes(2));

        using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        int deleted = LogRetention.Prune(_dir, LogFileNames.MainPattern, 1);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(1, deleted);
            Assert.True(File.Exists(locked));
        }
        else
        {
            Assert.Equal(2, deleted);
            Assert.False(File.Exists(locked));
        }
    }

    [Fact]
    public void MissingDirectoryOrZeroKeepIsNoop()
    {
        Assert.Equal(0, LogRetention.Prune(Path.Combine(_dir, "missing"), "*", 5));
        Touch("vrcft_2026-01-01_00-00-00.log", DateTime.UtcNow);
        Assert.Equal(0, LogRetention.Prune(_dir, LogFileNames.MainPattern, 0));
    }
}
