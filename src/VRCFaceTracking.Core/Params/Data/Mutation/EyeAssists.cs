using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed partial class EyeAssists : TrackingMutation
{
    public override string Name => "Eye Assists";
    public override string Description => "Gaze convergence fix, eye range limit, eye close assist, eyelid blend, eyelid sync with wink preservation, and force eyes closed.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int Order => 10;
    public override int PageOrder => 0;
    public override bool IsActive { get; set; }

    [MutationProperty("Gaze Convergence Fix", true, Group = "Gaze")]
    public bool ConvergenceFix { get; set; }
    [MutationProperty("Convergence Strength", true, Group = "Gaze", Indented = true)]
    public float ConvergenceStrength { get; set; } = 0.6f;

    [MutationProperty("Limit Eye Range", true, Group = "Gaze")]
    public bool LimitEyeRange { get; set; }
    [MutationProperty("Max Horizontal", true, Group = "Gaze", Indented = true)]
    public float MaxHorizontal { get; set; } = 0.35f;
    [MutationProperty("Max Vertical", true, Group = "Gaze", Indented = true)]
    public float MaxVertical { get; set; } = 0.35f;

    [MutationProperty("Eye Close Assist", true, Group = "Eyelids")]
    public bool CloseAssist { get; set; }
    [MutationProperty("Close Assist Strength", true, Group = "Eyelids", Indented = true)]
    public float CloseAssistStrength { get; set; } = 0.6f;

    [MutationProperty("Eyelid Blend", true, Group = "Eyelids")]
    public float EyeLidBlend { get; set; }

    [MutationProperty("Eyelid Sync", true, Group = "Eyelids")]
    public bool EyelidSync { get; set; }
    [MutationProperty("Eyelid Sync Strength", true, Group = "Eyelids", Indented = true)]
    public float SyncStrength { get; set; } = 0.7f;
    [MutationProperty("Sync to Most Open Eye", true, Group = "Eyelids", Indented = true)]
    public bool SyncToMostOpen { get; set; }
    [MutationProperty("Preserve Winks", true, Group = "Eyelids", Indented = true)]
    public bool PreserveWinks { get; set; } = true;

    [MutationProperty("Force Eyes Closed", true, Group = "Eyelids")]
    public bool ForceEyesClosed { get; set; }

    internal const string CorrectorsSettingName = "Unified Correctors";

    internal sealed class SavedEyelidBlend
    {
        public float? EyeLidBlend { get; set; }
    }

    internal async Task MoveEyelidBlendFromCorrectorsAsync(ILocalSettingsService settings, CancellationToken cancellationToken)
    {
        SavedEyelidBlend? saved = await settings.ReadSettingAsync<SavedEyelidBlend>(Name, defaultValue: null, forceLocal: true, cancellationToken).ConfigureAwait(false);
        if (saved?.EyeLidBlend != null)
        {
            return;
        }

        SavedEyelidBlend? old = await settings.ReadSettingAsync<SavedEyelidBlend>(CorrectorsSettingName, defaultValue: null, forceLocal: true, cancellationToken).ConfigureAwait(false);
        if (old?.EyeLidBlend is not { } blend)
        {
            return;
        }

        EyeLidBlend = blend;
        LogEyelidBlendMoved(Logger, blend, CorrectorsSettingName, Name);
    }

    private const double FullRangeRadians = Math.PI / 4;
    private const double RangeKneeShare = 0.75;

    internal Func<double> NowSeconds { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private double _lastTime = -1;
    private double _winkStart = -1;
    private float _smoothLeft;
    private float _smoothRight;
    private bool _smoothInit;

    public override void MutateData(ref UnifiedTrackingData data)
    {
        double now = NowSeconds();
        double dt = _lastTime < 0 ? double.MaxValue : now - _lastTime;
        _lastTime = now;

        if (EyeLidBlend > 0f)
        {
            ApplyEyelidBlend(ref data);
        }

        if (ConvergenceFix)
        {
            ApplyConvergence(ref data);
        }

        if (LimitEyeRange)
        {
            data.Eye.Left.Gaze.x = LimitAxis(data.Eye.Left.Gaze.x, MaxHorizontal);
            data.Eye.Left.Gaze.y = LimitAxis(data.Eye.Left.Gaze.y, MaxVertical);
            data.Eye.Right.Gaze.x = LimitAxis(data.Eye.Right.Gaze.x, MaxHorizontal);
            data.Eye.Right.Gaze.y = LimitAxis(data.Eye.Right.Gaze.y, MaxVertical);
        }

        if (CloseAssist)
        {
            data.Eye.Left.Openness = CloseKnee(data.Eye.Left.Openness);
            data.Eye.Right.Openness = CloseKnee(data.Eye.Right.Openness);
        }

        if (EyelidSync)
        {
            ApplyEyelidSync(ref data, now, dt);
        }

        if (ForceEyesClosed)
        {
            data.Eye.Left.Openness = 0f;
            data.Eye.Right.Openness = 0f;
            data.Eye.Left.Gaze.x = 0f;
            data.Eye.Left.Gaze.y = 0f;
            data.Eye.Right.Gaze.x = 0f;
            data.Eye.Right.Gaze.y = 0f;
            data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = 0f;
            data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = 0f;
        }
    }

    private void ApplyEyelidBlend(ref UnifiedTrackingData data)
    {
        BlendPair(ref data.Eye.Left.Openness, ref data.Eye.Right.Openness);
        BlendPair(ref data.Eye.Left.PupilDiameter_MM, ref data.Eye.Right.PupilDiameter_MM, float.MaxValue);
        BlendShapes(ref data, UnifiedExpressions.EyeWideLeft, UnifiedExpressions.EyeWideRight);
        BlendShapes(ref data, UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight);
        BlendShapes(ref data, UnifiedExpressions.BrowPinchLeft, UnifiedExpressions.BrowPinchRight);
        BlendShapes(ref data, UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowLowererRight);
        BlendShapes(ref data, UnifiedExpressions.BrowInnerUpLeft, UnifiedExpressions.BrowInnerUpRight);
        BlendShapes(ref data, UnifiedExpressions.BrowOuterUpLeft, UnifiedExpressions.BrowOuterUpRight);
    }

    private void BlendShapes(ref UnifiedTrackingData data, UnifiedExpressions left, UnifiedExpressions right) =>
        BlendPair(ref data.Shapes[(int)left].Weight, ref data.Shapes[(int)right].Weight);

    private void BlendPair(ref float left, ref float right, float max = 1f)
    {
        float share = Math.Clamp(EyeLidBlend, 0f, 1f) * 0.5f;
        float originalLeft = left;
        left = Math.Clamp((left * (1f - share)) + (right * share), 0f, max);
        right = Math.Clamp((right * (1f - share)) + (originalLeft * share), 0f, max);
    }

    private void ApplyConvergence(ref UnifiedTrackingData data)
    {
        float mean = (data.Eye.Left.Gaze.x + data.Eye.Right.Gaze.x) / 2f;
        float vergence = (data.Eye.Left.Gaze.x - data.Eye.Right.Gaze.x) / 2f;
        if (vergence < 0f)
        {
            vergence *= 1f - Math.Clamp(ConvergenceStrength, 0f, 1f);
        }
        data.Eye.Left.Gaze.x = mean + vergence;
        data.Eye.Right.Gaze.x = mean - vergence;
    }

    private static float LimitAxis(float gaze, float max)
    {
        float limit = Math.Clamp(max, 0f, 1f);
        if (limit >= 1f)
        {
            return gaze;
        }
        if (limit <= 0f)
        {
            return 0f;
        }

        double share = Math.Atan(gaze) / FullRangeRadians;
        double knee = RangeKneeShare * limit;
        double magnitude = Math.Abs(share);
        if (magnitude <= knee)
        {
            return gaze;
        }

        double span = limit - knee;
        double eased = knee + (span * Math.Tanh((magnitude - knee) / span));
        return (float)Math.Tan(Math.CopySign(eased, share) * FullRangeRadians);
    }

    private float CloseKnee(float openness)
    {
        float t = Math.Clamp(CloseAssistStrength, 0f, 1f) * 0.5f;
        return Math.Clamp((openness - t) / (1f - t), 0f, 1f);
    }

    private void ApplyEyelidSync(ref UnifiedTrackingData data, double now, double dt)
    {
        float lidLeft = data.Eye.Left.Openness;
        float lidRight = data.Eye.Right.Openness;

        if (PreserveWinks && Math.Abs(lidLeft - lidRight) > 0.45f)
        {
            if (_winkStart < 0)
            {
                _winkStart = now;
            }
            if (now - _winkStart >= 0.12)
            {
                _smoothLeft = lidLeft;
                _smoothRight = lidRight;
                return;
            }
        }
        else
        {
            _winkStart = -1;
        }

        float k = Math.Clamp(SyncStrength, 0f, 1f);
        float target = SyncToMostOpen ? Math.Max(lidLeft, lidRight) : Math.Min(lidLeft, lidRight);
        float blendedLeft = lidLeft + (k * (target - lidLeft));
        float blendedRight = lidRight + (k * (target - lidRight));

        if (!_smoothInit || dt > 0.5)
        {
            _smoothLeft = blendedLeft;
            _smoothRight = blendedRight;
            _smoothInit = true;
        }
        else
        {
            double step = Math.Clamp(dt, 0.001, 0.5);
            _smoothLeft = SmoothToward(_smoothLeft, blendedLeft, step);
            _smoothRight = SmoothToward(_smoothRight, blendedRight, step);
        }

        data.Eye.Left.Openness = Math.Clamp(_smoothLeft, 0f, 1f);
        data.Eye.Right.Openness = Math.Clamp(_smoothRight, 0f, 1f);

        float wideLeft = data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight;
        float wideRight = data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight;
        float wideTarget = SyncToMostOpen ? Math.Max(wideLeft, wideRight) : Math.Min(wideLeft, wideRight);
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = wideLeft + (k * (wideTarget - wideLeft));
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = wideRight + (k * (wideTarget - wideRight));
    }

    private static float SmoothToward(float current, float target, double dt)
    {
        double tau = target < current ? 0.018 : 0.070;
        double alpha = 1.0 - Math.Exp(-dt / tau);
        return current + ((float)alpha * (target - current));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Moved Eyelid Blend {Blend} from {From} to {To}")]
    private static partial void LogEyelidBlendMoved(ILogger logger, float blend, string from, string to);
}
