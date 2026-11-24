using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data;

public sealed record AvatarGapEntry(UnifiedExpressions Shape, AvatarGapStatus Status, string? FillSource)
{
    public string Name => SignalShapes.SignalName(Shape);
}
