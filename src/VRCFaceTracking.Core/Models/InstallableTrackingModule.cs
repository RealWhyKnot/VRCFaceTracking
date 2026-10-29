using Newtonsoft.Json;

namespace VRCFaceTracking.Core.Models;

public sealed class InstallableTrackingModule : TrackingModuleMetadata
{
    public InstallState InstallationState
    {
        get; set;
    }

    [JsonIgnore]
    public string AssemblyLoadPath
    {
        get; set;
    } = string.Empty;
}
