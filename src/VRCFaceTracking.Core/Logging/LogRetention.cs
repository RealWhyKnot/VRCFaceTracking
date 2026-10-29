using System.Diagnostics;

namespace VRCFaceTracking.Core.Logging;

public static class LogRetention
{
    public static int Prune(string directory, string searchPattern, int keep)
    {
        if (keep < 1 || !Directory.Exists(directory))
        {
            return 0;
        }

        IEnumerable<FileInfo> files = Directory.EnumerateFiles(directory, searchPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(keep);

        int deleted = 0;
        foreach (FileInfo? file in files)
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
