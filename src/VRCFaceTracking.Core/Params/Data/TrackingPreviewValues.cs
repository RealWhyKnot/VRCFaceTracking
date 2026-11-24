using System.Runtime.InteropServices;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data;

[StructLayout(LayoutKind.Auto)]
public readonly record struct TrackingPreviewValues(
    float EyeOpennessLeft,
    float EyeOpennessRight,
    float GazeX,
    float GazeY,
    float JawOpen,
    float MouthSmile,
    float EyeSquint,
    float CheekSquint)
{
    internal static TrackingPreviewValues From(UnifiedTrackingData data) => new(
        data.Eye.Left.Openness,
        data.Eye.Right.Openness,
        (data.Eye.Left.Gaze.x + data.Eye.Right.Gaze.x) * 0.5f,
        (data.Eye.Left.Gaze.y + data.Eye.Right.Gaze.y) * 0.5f,
        data.Shapes[(int)UnifiedExpressions.JawOpen].Weight,
        Average(data, UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.MouthCornerPullRight),
        Average(data, UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight),
        Average(data, UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.CheekSquintRight));

    private static float Average(UnifiedTrackingData data, UnifiedExpressions left, UnifiedExpressions right) =>
        (data.Shapes[(int)left].Weight + data.Shapes[(int)right].Weight) * 0.5f;
}
