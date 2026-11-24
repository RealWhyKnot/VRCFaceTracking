using System.Diagnostics;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class MouthBrowAssists : TrackingMutation
{
    public override string Name => "Mouth & Brow Assists";
    public override string Description => "Closes a lightly hanging mouth after you stop moving it.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int Order => 20;
    public override int PageOrder => 1;
    public override bool IsActive { get; set; }

    [MutationProperty("Idle Mouth Auto-Close", true, Group = "Mouth")]
    public bool IdleMouthAutoClose { get; set; }
    [MutationProperty("Idle Close Strength", true, Group = "Mouth", Indented = true)]
    public float IdleCloseStrength { get; set; } = 0.5f;

    internal Func<double> NowSeconds { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private double _lastTime = -1;
    private double _idleSince = -1;

    private static readonly UnifiedExpressions[] IdleActivityShapes =
    [
        UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.MouthCornerPullRight,
        UnifiedExpressions.MouthFrownLeft, UnifiedExpressions.MouthFrownRight,
        UnifiedExpressions.MouthStretchLeft, UnifiedExpressions.MouthStretchRight,
        UnifiedExpressions.LipFunnelUpperLeft, UnifiedExpressions.LipFunnelUpperRight,
        UnifiedExpressions.LipFunnelLowerLeft, UnifiedExpressions.LipFunnelLowerRight,
        UnifiedExpressions.LipPuckerUpperLeft, UnifiedExpressions.LipPuckerUpperRight,
    ];

    public override void MutateData(ref UnifiedTrackingData data)
    {
        double now = NowSeconds();
        double dt = _lastTime < 0 ? double.MaxValue : now - _lastTime;
        _lastTime = now;

        if (IdleMouthAutoClose)
        {
            ApplyIdleClose(ref data, now, dt);
        }
        else
        {
            _idleSince = -1;
        }
    }

    private void ApplyIdleClose(ref UnifiedTrackingData data, double now, double dt)
    {
        float jaw = data.Shapes[(int)UnifiedExpressions.JawOpen].Weight;
        UnifiedTrackingData local = data;
        bool candidate = jaw >= 0.08f && jaw <= 0.28f
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
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = jaw * (1f - Math.Clamp(IdleCloseStrength, 0f, 1f));
            MarkTriggered();
        }
    }
}
