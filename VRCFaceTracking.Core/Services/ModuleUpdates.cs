using VRCFaceTracking.Core.Models;

namespace VRCFaceTracking.Core.Services;

public static class ModuleUpdates
{
    public static bool IsUpdate(TrackingModuleMetadata remote, TrackingModuleMetadata installed)
    {
        if (remote.ModuleId != installed.ModuleId || installed.IsLocal)
        {
            return false;
        }

        try
        {
            return new Version(remote.Version).CompareTo(new Version(installed.Version)) > 0;
        }
        catch
        {
            return string.CompareOrdinal(remote.Version, installed.Version) > 0;
        }
    }
}
