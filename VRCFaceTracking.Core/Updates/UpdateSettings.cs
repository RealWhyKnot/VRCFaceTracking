using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Core.Updates;

public class UpdateSettings
{
    [SavedSetting("UpdateCheckOnStartup", true)]
    public bool CheckOnStartup { get; set; } = true;

    [SavedSetting("UpdateSkippedTag", "")]
    public string SkippedTag { get; set; } = string.Empty;
}
