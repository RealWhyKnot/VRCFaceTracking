using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Params.Expressions.Legacy.Lip;

internal interface ICombinedShape
{
    float GetBlendedLipShape(UnifiedTrackingData inputMap);
}
