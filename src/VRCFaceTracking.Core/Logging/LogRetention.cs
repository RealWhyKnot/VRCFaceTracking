using System.Diagnostics;

namespace VRCFaceTracking.Core.Logging;

public static class LogRetention
{
    public const long DefaultMaxTotalBytes = 100L * 1024 * 1024;

    public static int Prune(string directory, string searchPattern, int keep, long maxTotalBytes = DefaultMaxTotalBytes)
    {
        if (keep < 1 || !Directory.Exists(directory))
        {
            return 0;
        }

        IEnumerable<FileInfo> files = NewestFirst(directory, [searchPattern]).Skip(keep);
        int deleted = TryDelete(files);
        return deleted + EnforceSizeCap(directory, maxTotalBytes);
    }

    public static int EnforceSizeCap(string directory, long maxTotalBytes)
    {
        if (maxTotalBytes < 1 || !Directory.Exists(directory))
        {
            return 0;
        }

        List<FileInfo> files = NewestFirst(directory, [LogFileNames.MainPattern, LogFileNames.ModulePattern]);
        List<FileInfo> overflow = [];
        long total = 0;
        for (int i = 0; i < files.Count; i++)
        {
            long length = SafeLength(files[i]);
            if (overflow.Count > 0 || (i > 0 && total + length > maxTotalBytes))
            {
                overflow.Add(files[i]);
                continue;
            }

            total += length;
        }

        return TryDelete(overflow);
    }

    private static List<FileInfo> NewestFirst(string directory, string[] patterns) =>
        [.. patterns
            .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)];

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static int TryDelete(IEnumerable<FileInfo> files)
    {
        int deleted = 0;
        foreach (FileInfo file in files)
        {
            try
            {
                file.Delete();
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Old log {file.Name} could not be deleted: {e.Message}");
            }
        }

        return deleted;
    }
}
