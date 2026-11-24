using System.Runtime.InteropServices;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

[StructLayout(LayoutKind.Auto)]
public readonly record struct CalibrationStatus(CalibrationState State, float Progress, int LearnedShapeCount, int TotalShapeCount)
{
    public string Text => State switch
    {
        CalibrationState.Off => "Off",
        CalibrationState.Learning => "Learning",
        CalibrationState.Ready => "Ready",
        _ => "Learning",
    };
}
