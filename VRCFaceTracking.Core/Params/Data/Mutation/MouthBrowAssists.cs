using System;
using System.Diagnostics;
using System.Linq;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public class MouthBrowAssists : TrackingMutation
{
    public override string Name => "Mouth & Brow Assists";
    public override string Description => "Mouth close compensation, smile open assist, idle mouth auto-close, and eyelid-brow sync.";
    public override MutationPriority Step => MutationPriority.Postprocessor;
    public override int Order => 20;
    public override bool IsActive { get; set; } = true;

    [MutationProperty("Mouth Close Compensation", true)]
    public bool mouthCloseCompensation = false;

    [MutationProperty("Smile Open Assist", true)]
    public bool smileOpenAssist = false;
    [MutationProperty("Smile Assist Strength", true)]
    public float smileAssistStrength = 0.5f;

    [MutationProperty("Idle Mouth Auto-Close", true)]
    public bool idleMouthAutoClose = false;
    [MutationProperty("Idle Close Strength", true)]
    public float idleCloseStrength = 0.5f;

    [MutationProperty("Eyelid-Brow Sync", true)]
    public bool browSync = false;
    [MutationProperty("Brow Sync Strength", true)]
    public float browSyncStrength = 0.5f;

    internal Func<double> NowSeconds = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private double _lastTime = -1;
    private double _idleSince = -1;

    private static readonly UnifiedExpressions[] IdleActivityShapes =
    {
        UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.MouthCornerPullRight,
        UnifiedExpressions.MouthFrownLeft, UnifiedExpressions.MouthFrownRight,
        UnifiedExpressions.MouthStretchLeft, UnifiedExpressions.MouthStretchRight,
        UnifiedExpressions.LipFunnelUpperLeft, UnifiedExpressions.LipFunnelUpperRight,
        UnifiedExpressions.LipFunnelLowerLeft, UnifiedExpressions.LipFunnelLowerRight,
        UnifiedExpressions.LipPuckerUpperLeft, UnifiedExpressions.LipPuckerUpperRight
    };

    public override void MutateData(ref UnifiedTrackingData data)
    {
        var now = NowSeconds();
        var dt = _lastTime < 0 ? double.MaxValue : now - _lastTime;
        _lastTime = now;

        if (mouthCloseCompensation)
        {
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = Math.Max(0f,
                data.Shapes[(int)UnifiedExpressions.JawOpen].Weight
                - 0.6f * data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight);
        }

        if (smileOpenAssist)
        {
            var smile = Math.Max(
                data.Shapes[(int)UnifiedExpressions.MouthCornerPullLeft].Weight,
                data.Shapes[(int)UnifiedExpressions.MouthCornerPullRight].Weight);
            var assist = SmoothStep(0.35f, 1f, smile) * 0.18f * Math.Clamp(smileAssistStrength, 0f, 1f);
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight =
                Math.Max(data.Shapes[(int)UnifiedExpressions.JawOpen].Weight, assist);
        }

        if (idleMouthAutoClose)
        {
            ApplyIdleClose(ref data, now, dt);
        }
        else
        {
            _idleSince = -1;
        }

        if (browSync)
        {
            ApplyBrowSide(ref data, data.Eye.Left.Openness,
                UnifiedExpressions.BrowInnerUpLeft, UnifiedExpressions.BrowOuterUpLeft,
                UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowPinchLeft);
            ApplyBrowSide(ref data, data.Eye.Right.Openness,
                UnifiedExpressions.BrowInnerUpRight, UnifiedExpressions.BrowOuterUpRight,
                UnifiedExpressions.BrowLowererRight, UnifiedExpressions.BrowPinchRight);
        }
    }

    private void ApplyIdleClose(ref UnifiedTrackingData data, double now, double dt)
    {
        var jaw = data.Shapes[(int)UnifiedExpressions.JawOpen].Weight;
        var local = data;
        var candidate = jaw >= 0.08f && jaw <= 0.28f
            && data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight <= 0.20f
            && IdleActivityShapes.Max(s => local.Shapes[(int)s].Weight) <= 0.20f;

        if (!candidate)
        {
            _idleSince = -1;
            return;
        }
        if (_idleSince < 0 || dt >= 0.25)
        {
            _idleSince = now;
            return;
        }
        if (now - _idleSince >= 1.2)
        {
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = jaw * (1f - Math.Clamp(idleCloseStrength, 0f, 1f));
        }
    }

    private void ApplyBrowSide(ref UnifiedTrackingData data, float openness,
        UnifiedExpressions innerUp, UnifiedExpressions outerUp,
        UnifiedExpressions lowerer, UnifiedExpressions pinch)
    {
        var influence = Math.Clamp(browSyncStrength, 0f, 1f) * (1f - Math.Clamp(openness, 0f, 1f));
        data.Shapes[(int)innerUp].Weight *= 1f - 0.4f * influence;
        data.Shapes[(int)outerUp].Weight *= 1f - 0.4f * influence;
        data.Shapes[(int)lowerer].Weight = Math.Max(data.Shapes[(int)lowerer].Weight, 0.12f * influence);
        data.Shapes[(int)pinch].Weight = Math.Max(data.Shapes[(int)pinch].Weight, 0.06f * influence);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
