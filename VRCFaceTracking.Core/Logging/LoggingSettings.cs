using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Core.Logging;

public class LoggingSettings
{
    public const int DefaultLogFilesToKeep = 20;

    [SavedSetting("VerboseLogging", false)]
    public bool VerboseLogging { get; set; }

    [SavedSetting("LogFilesToKeep", DefaultLogFilesToKeep)]
    public int LogFilesToKeep { get; set; } = DefaultLogFilesToKeep;

    public bool VerboseEffective => BuildInfo.VerboseForced || VerboseLogging;
}
