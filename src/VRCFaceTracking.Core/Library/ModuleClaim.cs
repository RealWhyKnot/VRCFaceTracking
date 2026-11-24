using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Library;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ModuleClaim(string Name, bool EyeInitialized, bool ExpressionInitialized, TrackingCapability? AllowedOverride, TrackingCapability Effective)
{
    public TrackingCapability Allowed => AllowedOverride ?? TrackingCapability.All;
}
