using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data;

internal sealed record SignalGroup(UnifiedExpressions Primary, UnifiedExpressions Partner, string Name);
