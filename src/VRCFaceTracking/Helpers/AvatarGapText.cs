using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.Helpers;

internal static class AvatarGapText
{
    public static string? Describe(AvatarGapReport? report)
    {
        if (report is null || report.IsEmpty || report.NativeSkipCount == report.TrackedCount)
        {
            return null;
        }

        int missing = report.AbsentCount;
        int filled = report.FilledCount;
        return (missing, filled) switch
        {
            (0, _) => Resources.AvatarGap_AllSupported,
            (1, 0) => Resources.AvatarGap_Missing_One,
            (_, 0) => TrackingText.Format(Resources.AvatarGap_Missing_Many, missing),
            (1, _) => Resources.AvatarGap_MissingFilled_One,
            _ => TrackingText.Format(Resources.AvatarGap_MissingFilled_Many, missing, filled),
        };
    }
}
