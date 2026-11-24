using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Services;

public sealed record ExternalOscTarget(IReadOnlyList<UnifiedExpressions> Shapes, ExternalOscEyeChannel? EyeChannel);
