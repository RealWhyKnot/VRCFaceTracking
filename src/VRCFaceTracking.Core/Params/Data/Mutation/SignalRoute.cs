using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed record SignalRoute(
    string Id,
    SignalRouteKind Kind,
    UnifiedExpressions? SourceLeft,
    UnifiedExpressions? SourceRight,
    UnifiedExpressions TargetLeft,
    UnifiedExpressions TargetRight,
    IReadOnlyList<string> SourceNames,
    IReadOnlyList<string> TargetNames,
    int Feature,
    bool Strict)
{
    public const int CheekFeature = 0;
    public const int DimpleFeature = 1;
    public const int NoseMouthFeature = 2;
    public const int BrowFeature = 3;
    public const int EyeLidFeature = 4;
}
