using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

[MutationCard(TrackingCard.Smoothing)]
public sealed class Filter : TrackingMutation
{
    internal const float DefaultSmoothing = 0.5f;
    internal const float DefaultMinCutoff = 1f;
    internal const float DefaultBeta = 0.5f;
    internal const float DefaultDerivativeCutoff = 0.1f;
    private const float MinimumCutoff = 0.001f;

    private readonly EuroFilter[] _shapes;
    private readonly EuroFilter _gazeLeftX;
    private readonly EuroFilter _gazeLeftY;
    private readonly EuroFilter _gazeRightX;
    private readonly EuroFilter _gazeRightY;
    private readonly EuroFilter _pupilLeft;
    private readonly EuroFilter _pupilRight;
    private readonly EuroFilter _opennessLeft;
    private readonly EuroFilter _opennessRight;
    private readonly EuroFilter _headYaw;
    private readonly EuroFilter _headPitch;
    private readonly EuroFilter _headRoll;
    private readonly EuroFilter _headPosX;
    private readonly EuroFilter _headPosY;
    private readonly EuroFilter _headPosZ;

    private float _minCutoff = DefaultMinCutoff;
    private float _beta = DefaultBeta;

    public Filter()
    {
        _shapes = new EuroFilter[(int)UnifiedExpressions.Max];
        for (int i = 0; i < _shapes.Length; i++)
        {
            _shapes[i] = new EuroFilter(this);
        }

        _gazeLeftX = new EuroFilter(this);
        _gazeLeftY = new EuroFilter(this);
        _gazeRightX = new EuroFilter(this);
        _gazeRightY = new EuroFilter(this);
        _pupilLeft = new EuroFilter(this);
        _pupilRight = new EuroFilter(this);
        _opennessLeft = new EuroFilter(this);
        _opennessRight = new EuroFilter(this);
        _headYaw = new EuroFilter(this);
        _headPitch = new EuroFilter(this);
        _headRoll = new EuroFilter(this);
        _headPosX = new EuroFilter(this);
        _headPosY = new EuroFilter(this);
        _headPosZ = new EuroFilter(this);
    }

    [MutationProperty("Smoothing", true, Step = 0.05f)]
    public float Smoothing
    {
        get;
        set
        {
            field = SanitizeSmoothing(value);
            _minCutoff = MinCutoffFor(field);
            _beta = BetaFor(field);
        }
    } = DefaultSmoothing;

    [MutationProperty("Minimum Cutoff", true, 0f, 2f, Developer = true, Advanced = true, Step = 0.01f)]
    public float MinCutoff
    {
        get => _minCutoff;
        set => _minCutoff = float.IsFinite(value) ? Math.Max(value, MinimumCutoff) : DefaultMinCutoff;
    }

    [MutationProperty("Beta", true, Developer = true, Advanced = true, Step = 0.01f)]
    public float Beta
    {
        get => _beta;
        set => _beta = float.IsFinite(value) ? Math.Max(value, 0f) : DefaultBeta;
    }

    [MutationProperty("Derivative Cutoff", true, Developer = true, Advanced = true, Step = 0.01f)]
    public float DerivativeCutoff
    {
        get;
        set => field = float.IsFinite(value) ? Math.Max(value, MinimumCutoff) : DefaultDerivativeCutoff;
    } = DefaultDerivativeCutoff;

    internal static float MinCutoffFor(float smoothing) => DefaultMinCutoff * MathF.Pow(2f, 1f - (2f * SanitizeSmoothing(smoothing)));

    internal static float BetaFor(float smoothing) => DefaultBeta * MathF.Pow(2f, 1f - (2f * SanitizeSmoothing(smoothing)));

    private static float SanitizeSmoothing(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : DefaultSmoothing;

    public override string Name => "Data Filter";
    public override string Description => "Default data filtering for VRCFaceTracking expressions.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int Order => 100;
    public override int PageOrder => 7;

    public override void MutateData(ref UnifiedTrackingData data)
    {
        for (int i = 0; i < _shapes.Length; i++)
        {
            data.Shapes[i].Weight = _shapes[i].Apply(data.Shapes[i].Weight);
        }

        data.Eye.Left.Openness = _opennessLeft.Apply(data.Eye.Left.Openness);
        data.Eye.Left.PupilDiameter_MM = _pupilLeft.Apply(data.Eye.Left.PupilDiameter_MM);
        data.Eye.Left.Gaze.x = _gazeLeftX.Apply(data.Eye.Left.Gaze.x);
        data.Eye.Left.Gaze.y = _gazeLeftY.Apply(data.Eye.Left.Gaze.y);

        data.Eye.Right.Openness = _opennessRight.Apply(data.Eye.Right.Openness);
        data.Eye.Right.PupilDiameter_MM = _pupilRight.Apply(data.Eye.Right.PupilDiameter_MM);
        data.Eye.Right.Gaze.x = _gazeRightX.Apply(data.Eye.Right.Gaze.x);
        data.Eye.Right.Gaze.y = _gazeRightY.Apply(data.Eye.Right.Gaze.y);

        data.Head.HeadYaw = _headYaw.Apply(data.Head.HeadYaw);
        data.Head.HeadPitch = _headPitch.Apply(data.Head.HeadPitch);
        data.Head.HeadRoll = _headRoll.Apply(data.Head.HeadRoll);
        data.Head.HeadPosX = _headPosX.Apply(data.Head.HeadPosX);
        data.Head.HeadPosY = _headPosY.Apply(data.Head.HeadPosY);
        data.Head.HeadPosZ = _headPosZ.Apply(data.Head.HeadPosZ);
    }
}
