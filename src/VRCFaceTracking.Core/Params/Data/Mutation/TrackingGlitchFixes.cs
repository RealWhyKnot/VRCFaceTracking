using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed partial class TrackingGlitchFixes : TrackingMutation
{
    private const float SilentLevel = 0.02f;
    private const double SilentSeconds = 0.4;
    private const double QuietFadeSeconds = 0.15;
    private const double QuietReleaseSeconds = 0.05;
    private const float MaxTwitchTrim = 0.3f;
    private const float ClosedOpenness = 0.1f;
    private const double StuckSeconds = 20;
    private const int StuckBlinks = 4;
    private const float MinFreezeSeconds = 0.5f;
    private const float MaxFreezeSeconds = 10f;
    private const double RelaxSeconds = 1;
    private const double ReturnSeconds = 0.15;
    private const int EyeValues = 6;

    private static readonly UnifiedExpressions[] TwitchShapes =
    [
        UnifiedExpressions.JawOpen,
        UnifiedExpressions.LipPuckerUpperRight,
        UnifiedExpressions.LipPuckerUpperLeft,
        UnifiedExpressions.LipPuckerLowerRight,
        UnifiedExpressions.LipPuckerLowerLeft,
        UnifiedExpressions.LipFunnelUpperRight,
        UnifiedExpressions.LipFunnelUpperLeft,
        UnifiedExpressions.LipFunnelLowerRight,
        UnifiedExpressions.LipFunnelLowerLeft,
        UnifiedExpressions.MouthLowerDownRight,
        UnifiedExpressions.MouthLowerDownLeft,
    ];

    private readonly EyelidWatch _leftLid = new();
    private readonly EyelidWatch _rightLid = new();
    private float[] _raw = [];
    private float[] _previousRaw = [];
    private bool _hasPreviousRaw;
    private double _lastTime = -1;
    private double _lastVoiceAt = double.NaN;
    private double _rawChangedAt;
    private float _quiet;
    private float _relax;
    private bool _relaxing;
    private bool _coveringLeft;
    private bool _coveringRight;

    public override string Name => "Tracking Glitch Fixes";
    public override string Description => "Hides small mouth twitches while you're not talking, fills in for an eyelid that stops tracking, and relaxes your face when tracking freezes.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int Order => 1;
    public override int PageOrder => 3;
    public override bool IsActive { get; set; }

    [MutationProperty("Quiet Mouth While Silent", true, Group = "Mouth")]
    public bool QuietMouth { get; set; } = true;
    [MutationProperty("Twitch Trim", true, 0f, MaxTwitchTrim, Group = "Mouth", Indented = true)]
    public float TwitchTrim { get; set; } = 0.12f;

    [MutationProperty("Stuck Eyelid Fallback", true, Group = "Eyelids")]
    public bool StuckEyelidFallback { get; set; } = true;

    [MutationProperty("Relax When Tracking Freezes", true, Group = "Dropouts")]
    public bool RelaxOnFreeze { get; set; } = true;
    [MutationProperty("Seconds Before Relaxing", true, MinFreezeSeconds, MaxFreezeSeconds, Group = "Dropouts", Indented = true)]
    public float FreezeSeconds { get; set; } = 2f;

    [JsonIgnore]
    public override string StatusSummary
    {
        get
        {
            bool anyEnabled = IsActive && (QuietMouth || StuckEyelidFallback || RelaxOnFreeze);
            return !anyEnabled ? "Off"
                : _relaxing ? "Active: relaxing frozen tracking"
                : _coveringLeft || _coveringRight ? "Active: covering a stuck eyelid"
                : "Active";
        }
    }

    internal Func<double> NowSeconds { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal Func<UnifiedTrackingData> RawInput { get; set; } = () => UnifiedTracking.Data;
    internal Func<VoiceReading?> Voice { get; set; } = () => VoiceActivity.Latest;

    public override void MutateData(ref UnifiedTrackingData data)
    {
        double now = NowSeconds();
        double dt = _lastTime < 0 ? double.MaxValue : now - _lastTime;
        _lastTime = now;

        if (StuckEyelidFallback || RelaxOnFreeze)
        {
            ReadRaw();
        }

        if (QuietMouth)
        {
            QuietSilentMouth(data, now, dt);
        }

        if (StuckEyelidFallback)
        {
            CoverStuckEyelid(data, now);
        }

        if (RelaxOnFreeze)
        {
            RelaxFrozenFace(data, now, dt);
        }
    }

    internal static float Trim(float weight, float trim)
    {
        if (trim <= 0f || weight >= 3f * trim)
        {
            return weight;
        }

        float over = weight - trim;
        return over <= 0f ? 0f : (7f / (4f * trim) * over * over) - (1f / (2f * trim * trim) * over * over * over);
    }

    private static float Ramp(double elapsed, double span) =>
        elapsed <= 0 ? 0f : elapsed >= span ? 1f : (float)(elapsed / span);

    private static float Follow(float current, float target, double dt, double releaseSeconds) =>
        target >= current ? target : Math.Max(target, current - (float)(dt / releaseSeconds));

    private void ReadRaw()
    {
        using (UnifiedTracking.LockData())
        {
            UnifiedTrackingData raw = RawInput();
            int shapes = raw.Shapes.Length;
            if (_raw.Length != shapes + EyeValues)
            {
                _raw = new float[shapes + EyeValues];
                _previousRaw = new float[shapes + EyeValues];
                _hasPreviousRaw = false;
            }

            for (int i = 0; i < shapes; i++)
            {
                _raw[i] = raw.Shapes[i].Weight;
            }

            _raw[shapes] = raw.Eye.Left.Openness;
            _raw[shapes + 1] = raw.Eye.Right.Openness;
            _raw[shapes + 2] = raw.Eye.Left.Gaze.x;
            _raw[shapes + 3] = raw.Eye.Left.Gaze.y;
            _raw[shapes + 4] = raw.Eye.Right.Gaze.x;
            _raw[shapes + 5] = raw.Eye.Right.Gaze.y;
        }
    }

    private void QuietSilentMouth(UnifiedTrackingData data, double now, double dt)
    {
        VoiceReading? voice = Voice();
        float target = 0f;
        if (voice is { HasLevel: true, Muted: false })
        {
            if (double.IsNaN(_lastVoiceAt) || voice.Level >= SilentLevel)
            {
                _lastVoiceAt = now;
            }

            target = Ramp(now - _lastVoiceAt - SilentSeconds, QuietFadeSeconds);
        }
        else
        {
            _lastVoiceAt = double.NaN;
        }

        _quiet = Follow(_quiet, target, dt, QuietReleaseSeconds);
        if (_quiet <= 0f)
        {
            return;
        }

        MarkTriggered();
        float trim = Math.Clamp(TwitchTrim, 0f, MaxTwitchTrim) * _quiet;
        foreach (UnifiedExpressions shape in TwitchShapes)
        {
            data.Shapes[(int)shape].Weight = Trim(data.Shapes[(int)shape].Weight, trim);
        }
    }

    private void CoverStuckEyelid(UnifiedTrackingData data, double now)
    {
        int eyes = _raw.Length - EyeValues;
        float left = _raw[eyes];
        float right = _raw[eyes + 1];
        bool leftBlinked = _leftLid.TrackBlink(left);
        bool rightBlinked = _rightLid.TrackBlink(right);
        _leftLid.TrackStuck(left, now, rightBlinked);
        _rightLid.TrackStuck(right, now, leftBlinked);

        bool leftStuck = _leftLid.IsStuck(now);
        bool rightStuck = _rightLid.IsStuck(now);
        bool coverLeft = leftStuck && !rightStuck;
        bool coverRight = rightStuck && !leftStuck;

        if (coverLeft && !_coveringLeft)
        {
            LogCoveringEyelid(Logger, "left");
        }

        if (coverRight && !_coveringRight)
        {
            LogCoveringEyelid(Logger, "right");
        }

        _coveringLeft = coverLeft;
        _coveringRight = coverRight;

        if (coverLeft || coverRight)
        {
            MarkTriggered();
        }

        if (coverLeft)
        {
            data.Eye.Left.Openness = data.Eye.Right.Openness;
            CopyShape(data, UnifiedExpressions.EyeSquintRight, UnifiedExpressions.EyeSquintLeft);
            CopyShape(data, UnifiedExpressions.EyeWideRight, UnifiedExpressions.EyeWideLeft);
        }
        else if (coverRight)
        {
            data.Eye.Right.Openness = data.Eye.Left.Openness;
            CopyShape(data, UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight);
            CopyShape(data, UnifiedExpressions.EyeWideLeft, UnifiedExpressions.EyeWideRight);
        }
    }

    private static void CopyShape(UnifiedTrackingData data, UnifiedExpressions from, UnifiedExpressions to) =>
        data.Shapes[(int)to].Weight = data.Shapes[(int)from].Weight;

    private void RelaxFrozenFace(UnifiedTrackingData data, double now, double dt)
    {
        if (!_hasPreviousRaw || !_raw.AsSpan().SequenceEqual(_previousRaw))
        {
            _raw.CopyTo(_previousRaw, 0);
            _hasPreviousRaw = true;
            _rawChangedAt = now;
        }

        double frozenFor = now - _rawChangedAt;
        double hold = Math.Clamp(FreezeSeconds, MinFreezeSeconds, MaxFreezeSeconds);
        float target = Ramp(frozenFor - hold, RelaxSeconds);
        if (target > 0f && !_relaxing)
        {
            LogRelaxingFrozenFace(Logger, frozenFor);
        }

        _relaxing = target > 0f;
        _relax = Follow(_relax, target, dt, ReturnSeconds);
        if (_relax <= 0f)
        {
            return;
        }

        MarkTriggered();
        float keep = 1f - _relax;
        for (int i = 0; i < data.Shapes.Length; i++)
        {
            data.Shapes[i].Weight *= keep;
        }

        data.Eye.Left.Openness += (1f - data.Eye.Left.Openness) * _relax;
        data.Eye.Right.Openness += (1f - data.Eye.Right.Openness) * _relax;
        data.Eye.Left.Gaze.x *= keep;
        data.Eye.Left.Gaze.y *= keep;
        data.Eye.Right.Gaze.x *= keep;
        data.Eye.Right.Gaze.y *= keep;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The {Eye} eyelid stopped tracking, so it now follows the other eye")]
    private static partial void LogCoveringEyelid(ILogger logger, string eye);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking froze for {Seconds:0.0}s, relaxing the face")]
    private static partial void LogRelaxingFrozenFace(ILogger logger, double seconds);

    private sealed class EyelidWatch
    {
        private float _last = float.NaN;
        private double _changedAt;
        private int _partnerBlinks;
        private bool _closed;

        internal bool TrackBlink(float openness)
        {
            bool closed = openness < ClosedOpenness;
            bool blinked = closed && !_closed;
            _closed = closed;
            return blinked;
        }

        internal void TrackStuck(float openness, double now, bool partnerBlinked)
        {
            if (BitConverter.SingleToInt32Bits(openness) != BitConverter.SingleToInt32Bits(_last))
            {
                _last = openness;
                _changedAt = now;
                _partnerBlinks = 0;
                return;
            }

            if (partnerBlinked)
            {
                _partnerBlinks++;
            }
        }

        internal bool IsStuck(double now) => now - _changedAt >= StuckSeconds && _partnerBlinks >= StuckBlinks;
    }
}
