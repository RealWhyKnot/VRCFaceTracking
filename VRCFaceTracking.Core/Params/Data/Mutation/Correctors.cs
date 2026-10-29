using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public class Correctors : TrackingMutation
{
    public override string Name => "Unified Correctors";
    public override string Description => "Processes data to conform to Unified Expressions.";
    public override MutationPriority Step => MutationPriority.Postprocessor;
    public override int PageOrder => 4;
    public override bool IsActive { get; set; } = true;

    [MutationProperty("MouthClosed/JawOpen Clamp", true, Group = "Mouth")]
    public bool mouthClosedFix = true;
    [MutationProperty("LipSuck Limiter", true, Group = "Mouth")]
    public bool lipSuckFix = true;

    // For ppl with hardware that provides unpredictable eyeL/R movement
    [MutationProperty("EyeLook Symmetrize", true, Group = "Eyes")]
    public bool eyeLookSymmetrize = false;


    public override void MutateData(ref UnifiedTrackingData data)
    {
        if (mouthClosedFix)
        {
            data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight =
                Math.Min(
                    data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight,
                    data.Shapes[(int)UnifiedExpressions.JawOpen].Weight
                );
        }

        if (lipSuckFix)
        {
            data.Shapes[(int)UnifiedExpressions.LipSuckLowerLeft].Weight *= (1f - data.Shapes[(int)UnifiedExpressions.MouthLowerDownLeft].Weight);
            data.Shapes[(int)UnifiedExpressions.LipSuckLowerRight].Weight *= (1f - data.Shapes[(int)UnifiedExpressions.MouthLowerDownRight].Weight);
            data.Shapes[(int)UnifiedExpressions.LipSuckUpperLeft].Weight *= (1f - data.Shapes[(int)UnifiedExpressions.MouthUpperUpLeft].Weight);
            data.Shapes[(int)UnifiedExpressions.LipSuckUpperRight].Weight *= (1f - data.Shapes[(int)UnifiedExpressions.MouthUpperUpRight].Weight);
        }

        if (eyeLookSymmetrize)
        {
            // Easy for Y since human eyes can't rlly diverge that way. We'll just grab the median
            var combinedY = (data.Eye.Right.Gaze.y + data.Eye.Left.Gaze.y) / 2;
            data.Eye.Right.Gaze.y = combinedY;
            data.Eye.Left.Gaze.y = combinedY;

            //TODO: Figure out eyex convergence
        }
    }
}
