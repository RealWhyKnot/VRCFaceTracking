using System;
using System.Diagnostics;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public class EyeAssists : TrackingMutation
{
    public override string Name => "Eye Assists";
    public override string Description => "Gaze convergence fix, eye range limit, eye close assist, eyelid blend, eyelid sync with wink preservation, and force eyes closed.";
    public override MutationPriority Step => MutationPriority.Postprocessor;
    public override int Order => 10;
    public override bool IsActive { get; set; } = false;

    [MutationProperty("Gaze Convergence Fix", true)]
    public bool convergenceFix = false;
    [MutationProperty("Convergence Strength", true)]
    public float convergenceStrength = 0.6f;

    [MutationProperty("Limit Eye Range", true)]
    public bool limitEyeRange = false;
    [MutationProperty("Max Horizontal", true)]
    public float maxHorizontal = 0.35f;
    [MutationProperty("Max Vertical", true)]
    public float maxVertical = 0.35f;

    [MutationProperty("Eye Close Assist", true)]
    public bool closeAssist = false;
    [MutationProperty("Close Assist Strength", true)]
    public float closeAssistStrength = 0.6f;

    [MutationProperty("Eyelid Blend", true)]
    public float eyeLidBlend = 0f;

    [MutationProperty("Eyelid Sync", true)]
    public bool eyelidSync = false;
    [MutationProperty("Eyelid Sync Strength", true)]
    public float syncStrength = 0.7f;
    [MutationProperty("Sync to Most Open Eye", true)]
    public bool syncToMostOpen = false;
    [MutationProperty("Preserve Winks", true)]
    public bool preserveWinks = true;

    [MutationProperty("Force Eyes Closed", true)]
    public bool forceEyesClosed = false;

    private const double FullRangeRadians = Math.PI / 4;
    private const double RangeKneeShare = 0.75;

    internal Func<double> NowSeconds = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private double _lastTime = -1;
    private double _winkStart = -1;
    private float _smoothLeft;
    private float _smoothRight;
    private bool _smoothInit;

    public override void MutateData(ref UnifiedTrackingData data)
    {
        var now = NowSeconds();
        var dt = _lastTime < 0 ? double.MaxValue : now - _lastTime;
        _lastTime = now;

        if (eyeLidBlend > 0f)
        {
            ApplyEyelidBlend(ref data);
        }

        if (convergenceFix)
        {
            ApplyConvergence(ref data);
        }

        if (limitEyeRange)
        {
            data.Eye.Left.Gaze.x = LimitAxis(data.Eye.Left.Gaze.x, maxHorizontal);
            data.Eye.Left.Gaze.y = LimitAxis(data.Eye.Left.Gaze.y, maxVertical);
            data.Eye.Right.Gaze.x = LimitAxis(data.Eye.Right.Gaze.x, maxHorizontal);
            data.Eye.Right.Gaze.y = LimitAxis(data.Eye.Right.Gaze.y, maxVertical);
        }

        if (closeAssist)
        {
            data.Eye.Left.Openness = CloseKnee(data.Eye.Left.Openness);
            data.Eye.Right.Openness = CloseKnee(data.Eye.Right.Openness);
        }

        if (eyelidSync)
        {
            ApplyEyelidSync(ref data, now, dt);
        }

        if (forceEyesClosed)
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
        var share = Math.Clamp(eyeLidBlend, 0f, 1f) * 0.5f;
        var originalLeft = left;
        left = Math.Clamp(left * (1f - share) + right * share, 0f, max);
        right = Math.Clamp(right * (1f - share) + originalLeft * share, 0f, max);
    }

    private void ApplyConvergence(ref UnifiedTrackingData data)
    {
        var mean = (data.Eye.Left.Gaze.x + data.Eye.Right.Gaze.x) / 2f;
        var vergence = (data.Eye.Left.Gaze.x - data.Eye.Right.Gaze.x) / 2f;
        if (vergence < 0f)
        {
            vergence *= 1f - Math.Clamp(convergenceStrength, 0f, 1f);
        }
        data.Eye.Left.Gaze.x = mean + vergence;
        data.Eye.Right.Gaze.x = mean - vergence;
    }

    private static float LimitAxis(float gaze, float max)
    {
        var limit = Math.Clamp(max, 0f, 1f);
        if (limit >= 1f)
        {
            return gaze;
        }
        if (limit <= 0f)
        {
            return 0f;
        }

        var share = Math.Atan(gaze) / FullRangeRadians;
        var knee = RangeKneeShare * limit;
        var magnitude = Math.Abs(share);
        if (magnitude <= knee)
        {
            return gaze;
        }

        var span = limit - knee;
        var eased = knee + span * Math.Tanh((magnitude - knee) / span);
        return (float)Math.Tan(Math.CopySign(eased, share) * FullRangeRadians);
    }

    private float CloseKnee(float openness)
    {
        var t = Math.Clamp(closeAssistStrength, 0f, 1f) * 0.5f;
        return Math.Clamp((openness - t) / (1f - t), 0f, 1f);
    }

    private void ApplyEyelidSync(ref UnifiedTrackingData data, double now, double dt)
    {
        var lidLeft = data.Eye.Left.Openness;
        var lidRight = data.Eye.Right.Openness;

        if (preserveWinks && Math.Abs(lidLeft - lidRight) > 0.45f)
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

        var k = Math.Clamp(syncStrength, 0f, 1f);
        var target = syncToMostOpen ? Math.Max(lidLeft, lidRight) : Math.Min(lidLeft, lidRight);
        var blendedLeft = lidLeft + k * (target - lidLeft);
        var blendedRight = lidRight + k * (target - lidRight);

        if (!_smoothInit || dt > 0.5)
        {
            _smoothLeft = blendedLeft;
            _smoothRight = blendedRight;
            _smoothInit = true;
        }
        else
        {
            var step = Math.Clamp(dt, 0.001, 0.5);
            _smoothLeft = SmoothToward(_smoothLeft, blendedLeft, step);
            _smoothRight = SmoothToward(_smoothRight, blendedRight, step);
        }

        data.Eye.Left.Openness = Math.Clamp(_smoothLeft, 0f, 1f);
        data.Eye.Right.Openness = Math.Clamp(_smoothRight, 0f, 1f);

        var wideLeft = data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight;
        var wideRight = data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight;
        var wideTarget = syncToMostOpen ? Math.Max(wideLeft, wideRight) : Math.Min(wideLeft, wideRight);
        data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = wideLeft + k * (wideTarget - wideLeft);
        data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = wideRight + k * (wideTarget - wideRight);
    }

    private static float SmoothToward(float current, float target, double dt)
    {
        var tau = target < current ? 0.018 : 0.070;
        var alpha = 1.0 - Math.Exp(-dt / tau);
        return current + (float)alpha * (target - current);
    }
}
