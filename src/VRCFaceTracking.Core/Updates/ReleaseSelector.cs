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
        AppVersion bestVersion = current;
        foreach (GithubRelease release in releases)
        {
            if (release.Draft || (channel == BuildChannel.Release && release.Prerelease))
            {
                continue;
            }

            if (!AppVersion.TryParse(release.TagName, out AppVersion version) || version <= bestVersion)
            {
                continue;
            }

            best = release;
            bestVersion = version;
        }

        return best;
    }
}
