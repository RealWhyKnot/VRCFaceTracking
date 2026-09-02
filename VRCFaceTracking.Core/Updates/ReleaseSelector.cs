namespace VRCFaceTracking.Core.Updates;

public static class ReleaseSelector
{
    public static GithubRelease? Select(IEnumerable<GithubRelease> releases, AppVersion current, BuildChannel channel)
    {
        if (channel == BuildChannel.Dev)
        {
            return null;
        }

        GithubRelease? best = null;
        var bestVersion = current;
        foreach (var release in releases)
        {
            if (release.Draft || (channel == BuildChannel.Release && release.Prerelease))
            {
                continue;
            }

            if (!AppVersion.TryParse(release.TagName, out var version) || version.CompareTo(bestVersion) <= 0)
            {
                continue;
            }

            best = release;
            bestVersion = version;
        }

        return best;
    }
}
