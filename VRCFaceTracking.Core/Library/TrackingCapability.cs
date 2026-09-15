using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Library;

[Flags]
public enum TrackingCapability
{
    None = 0,
    Eyes = 1,
    Brows = 2,
    Mouth = 4,
    Tongue = 8,
    Head = 16,
    All = Eyes | Brows | Mouth | Tongue | Head,
}

public static class TrackingCapabilities
{
    public const TrackingCapability EyeHalf = TrackingCapability.Eyes | TrackingCapability.Brows;
    public const TrackingCapability ExpressionHalf = TrackingCapability.Mouth | TrackingCapability.Tongue;

    public static readonly (TrackingCapability Capability, int Start, int EndInclusive)[] ShapeRanges =
    {
        (TrackingCapability.Eyes, (int)UnifiedExpressions.EyeSquintRight, (int)UnifiedExpressions.EyeWideLeft),
        (TrackingCapability.Brows, (int)UnifiedExpressions.BrowPinchRight, (int)UnifiedExpressions.BrowOuterUpLeft),
        (TrackingCapability.Mouth, (int)UnifiedExpressions.NasalDilationRight, (int)UnifiedExpressions.MouthTightenerLeft),
        (TrackingCapability.Tongue, (int)UnifiedExpressions.TongueOut, (int)UnifiedExpressions.TongueTwistLeft),
        (TrackingCapability.Mouth, (int)UnifiedExpressions.SoftPalateClose, (int)UnifiedExpressions.NeckFlexLeft),
    };
}
