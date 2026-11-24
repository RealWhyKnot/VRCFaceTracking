using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed partial class Calibration : TrackingMutation
{
    internal const int DefaultWindowSize = 64;
    internal const int MinWindowSize = 8;
    internal const int MaxWindowSize = 1024;
    internal const float DefaultStepDelta = 0.15f;

    private const float FrameSeconds = 0.1f;
    private const float LearnedProgress = 0.999f;

    [MutationProperty("Window Size", true, Developer = true)]
    public int WindowSize
    {
        get;
        set => field = Math.Clamp(value, MinWindowSize, MaxWindowSize);
    } = DefaultWindowSize;

    [MutationProperty("Step Delta", true, Developer = true)]
    public float StepDelta
    {
        get;
        set => field = float.IsFinite(value) && value > 0f ? value : DefaultStepDelta;
    } = DefaultStepDelta;

    [MutationProperty("Calibration Blend", true)]
    public float CalibrationBlend { get; set; } = 1f;

    [MutationProperty("Continuous Calibration", true)]
    public bool ContinuousCalibration { get; set; }

    public CalibrationData CalData
    {
        get;
        set
        {
            field = value ?? new();
            ApplyWindowSize();
        }
    } = new();

    [JsonIgnore]
    public CalibrationStatus Status
    {
        get
        {
            IReadOnlyList<CalibrationParameter> shapes = CalData.Shapes;
            int total = shapes.Count;
            int learned = 0;
            int started = 0;
            float sum = 0f;
            for (int i = 0; i < total; i++)
            {
                float progress = shapes[i].Progress;
                sum += progress;
                if (progress > 0f)
                {
                    started++;
                }

                if (progress >= LearnedProgress)
                {
                    learned++;
                }
            }

            float aggregate = total == 0 ? 0f : Math.Clamp(sum / total, 0f, 1f);
            CalibrationState state = !IsActive
                ? CalibrationState.Off
                : learned > 0 && learned == started ? CalibrationState.Ready : CalibrationState.Learning;
            return new CalibrationStatus(state, aggregate, learned, total);
        }
    }

    [JsonIgnore]
    public float Progress => Status.Progress;

    [JsonIgnore]
    public int LearnedShapeCount => Status.LearnedShapeCount;

    [JsonIgnore]
    public int TotalShapeCount => CalData.Shapes.Count;

    [JsonIgnore]
    public string StatusText => Status.Text;

    public override string Name => "Calibration";
    public override string Description => "Normalizes tracking data.";
    public override MutationPriority Priority => MutationPriority.Preprocessor;
    public override int PageOrder => 5;

    public override void Initialize(UnifiedTrackingData data) => CalData ??= new();

    public override void MutateData(ref UnifiedTrackingData data)
    {
        IReadOnlyList<CalibrationParameter> shapes = CalData.Shapes;
        int count = Math.Min(shapes.Count, data.Shapes.Length);
        for (int i = 0; i < count; i++)
        {
            CalibrationParameter shape = shapes[i];
            shape.UpdateCalibration(data.Shapes[i].Weight, ContinuousCalibration, Logger, FrameSeconds, StepDelta, WindowSize);
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

    private void ApplyWindowSize()
    {
        IReadOnlyList<CalibrationParameter> shapes = CalData.Shapes;
        for (int i = 0; i < shapes.Count; i++)
        {
            shapes[i].Resize(WindowSize);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logging Calibration data: delta: {Delta} points: {Points}")]
    private static partial void LogWindow(ILogger logger, float delta, int points);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Shape}\n  max value: {Max}\n  raw value: {Raw}\n  weighted value: {Weighted}")]
    private static partial void LogShape(ILogger logger, UnifiedExpressions shape, float max, float raw, float weighted);
}
