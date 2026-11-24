using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data;

public sealed record AvatarSupport(bool UsesNativeFaceTracking, IReadOnlySet<UnifiedExpressions> DrivenShapes)
{
    public bool Drives(UnifiedExpressions shape) => DrivenShapes.Contains(shape);

    public bool Drives(UnifiedExpressions left, UnifiedExpressions right) => Drives(left) || Drives(right);

    internal static AvatarSupport From(AvatarParameterSnapshot avatar)
    {
        HashSet<UnifiedExpressions> driven = [];
        int count = (int)UnifiedExpressions.Max;
        for (int i = 0; i < count; i++)
        {
            var shape = (UnifiedExpressions)i;
            if (avatar.DrivesAny(SignalShapes.NamesOf(shape)))
            {
                driven.Add(shape);
            }
        }

        return new AvatarSupport(avatar.UsesNativeFaceTracking, driven);
    }
}
