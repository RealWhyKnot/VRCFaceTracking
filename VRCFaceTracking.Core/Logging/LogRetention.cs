namespace VRCFaceTracking.Core.Logging;

public static class LogRetention
{
    public static int Prune(string directory, string searchPattern, int keep)
    {
        if (keep < 1 || !Directory.Exists(directory))
        {
            return 0;
        }

        var files = Directory.EnumerateFiles(directory, searchPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(keep);

        var deleted = 0;
        foreach (var file in files)
        {
            try
            {
                file.Delete();
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }
}
