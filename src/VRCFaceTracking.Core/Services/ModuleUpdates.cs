using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Core.Services;

public static class ModuleUpdates
{
    public static bool IsUpdate(TrackingModuleMetadata remote, TrackingModuleMetadata installed) =>
        remote.ModuleId == installed.ModuleId && !installed.IsLocal && IsNewer(remote.Version, installed.Version);

    private static bool IsNewer(string remote, string installed) =>
        Version.TryParse(remote, out Version? remoteVersion) && Version.TryParse(installed, out Version? installedVersion)
            ? remoteVersion > installedVersion
            : string.CompareOrdinal(remote, installed) > 0;
}
