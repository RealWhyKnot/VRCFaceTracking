using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class Correctors : TrackingMutation
{
    public override string Name => "Unified Correctors";
    public override string Description => "Processes data to conform to Unified Expressions.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int PageOrder => 4;
    public override bool IsActive { get; set; } = true;

    [MutationProperty("MouthClosed/JawOpen Clamp", true, Group = "Mouth")]
    public bool MouthClosedFix { get; set; } = true;
    [MutationProperty("LipSuck Limiter", true, Group = "Mouth")]
    public bool LipSuckFix { get; set; } = true;

    [MutationProperty("EyeLook Symmetrize", true, Group = "Eyes")]
    public bool EyeLookSymmetrize { get; set; }


    public override void MutateData(ref UnifiedTrackingData data)
    {
        if (MouthClosedFix)
        {
            data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight =
                Math.Min(
                    data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight,
                    data.Shapes[(int)UnifiedExpressions.JawOpen].Weight
                );
        }

        if (LipSuckFix)
        {
            data.Shapes[(int)UnifiedExpressions.LipSuckLowerLeft].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthLowerDownLeft].Weight;
            data.Shapes[(int)UnifiedExpressions.LipSuckLowerRight].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthLowerDownRight].Weight;
            data.Shapes[(int)UnifiedExpressions.LipSuckUpperLeft].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthUpperUpLeft].Weight;
            data.Shapes[(int)UnifiedExpressions.LipSuckUpperRight].Weight *= 1f - data.Shapes[(int)UnifiedExpressions.MouthUpperUpRight].Weight;
        }

        if (EyeLookSymmetrize)
        {
            float combinedY = (data.Eye.Right.Gaze.y + data.Eye.Left.Gaze.y) / 2;
            data.Eye.Right.Gaze.y = combinedY;
            data.Eye.Left.Gaze.y = combinedY;
        }
    }
}
