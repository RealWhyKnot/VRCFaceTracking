using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed partial class Calibration : TrackingMutation
{
    private const float FrameSeconds = 0.1f;

    [MutationProperty("Window Size", Developer = true)]
    public static int WindowSize { get; set; } = 64;

    [MutationProperty("Step Delta", Developer = true)]
    public static float StepDelta { get; set; } = 0.15f;

    [MutationProperty("Calibration Blend", true)]
    public float CalibrationBlend { get; set; } = 1f;

    [MutationProperty("Continuous Calibration", true)]
    public bool ContinuousCalibration { get; set; }

    public CalibrationData CalData { get; set; } = new();

    public override string Name => "Calibration";
    public override string Description => "Normalizes tracking data.";
    public override MutationPriority Priority => MutationPriority.Preprocessor;
    public override int PageOrder => 3;
    public override bool IsSaved => true;

    public override void Initialize(UnifiedTrackingData data) => CalData ??= new();

    public override void MutateData(ref UnifiedTrackingData data)
    {
        IReadOnlyList<CalibrationParameter> shapes = CalData.Shapes;
        int count = Math.Min(shapes.Count, data.Shapes.Length);
        for (int i = 0; i < count; i++)
        {
            CalibrationParameter shape = shapes[i];
            shape.UpdateCalibration(data.Shapes[i].Weight, ContinuousCalibration, Logger, FrameSeconds);
            data.Shapes[i].Weight = shape.CalculateParameter(data.Shapes[i].Weight, CalibrationBlend);
        }
    }

    [MutationButton("Log Data", Developer = true)]
    public void LogData()
    {
        if (!Logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        LogWindow(Logger, StepDelta, WindowSize);
        IReadOnlyList<CalibrationParameter> shapes = CalData.Shapes;
        UnifiedExpressionShape[] live = UnifiedTracking.Data.Shapes;
        int count = Math.Min(shapes.Count, live.Length);
        for (int i = 0; i < count; i++)
        {
            float raw = live[i].Weight;
            float max = shapes[i].Max;
            float weighted = shapes[i].CalculateParameter(raw, CalibrationBlend);
            LogShape(Logger, (UnifiedExpressions)i, max, raw, weighted);
        }
    }

    [MutationButton("Reset Calibration")]
    public void ClearData() => CalData.Clear();

    [LoggerMessage(Level = LogLevel.Information, Message = "Logging Calibration data: delta: {Delta} points: {Points}")]
    private static partial void LogWindow(ILogger logger, float delta, int points);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Shape}\n  max value: {Max}\n  raw value: {Raw}\n  weighted value: {Weighted}")]
    private static partial void LogShape(ILogger logger, UnifiedExpressions shape, float max, float raw, float weighted);
}
