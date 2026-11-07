using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Core.Logging;

public sealed class LoggingSettings
{
    public const int DefaultLogFilesToKeep = 20;
    public const int ShapeLoggingFollowBuild = 0;
    public const int ShapeLoggingOn = 1;
    public const int ShapeLoggingOff = 2;

    [SavedSetting("VerboseLogging", false)]
    public bool VerboseLogging
    {
        get; set;
    }

    [SavedSetting("LogFilesToKeep", DefaultLogFilesToKeep)]
    public int LogFilesToKeep { get; set; } = DefaultLogFilesToKeep;

    [SavedSetting("ShapeLogging", ShapeLoggingFollowBuild)]
    public int ShapeLoggingChoice
    {
        get; set;
    }

    public bool VerboseEffective => BuildInfo.VerboseForced || VerboseLogging;

    public bool ShapeLoggingEffective => IsShapeLoggingOn(ShapeLoggingChoice, BuildInfo.Channel);

    public static bool IsShapeLoggingOn(int choice, BuildChannel channel) => choice switch
    {
        ShapeLoggingOn => true,
        ShapeLoggingOff => false,
        _ => channel != BuildChannel.Release,
    };
}
