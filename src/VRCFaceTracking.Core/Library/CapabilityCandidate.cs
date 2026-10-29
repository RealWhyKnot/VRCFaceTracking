namespace VRCFaceTracking.Core.Library;

public readonly record struct CapabilityCandidate(bool EyeInitialized, bool ExpressionInitialized, TrackingCapability? AllowedOverride)
{
    public TrackingCapability Allowed => AllowedOverride ?? TrackingCapability.All;
    public bool AnyInitialized => EyeInitialized || ExpressionInitialized;
}
