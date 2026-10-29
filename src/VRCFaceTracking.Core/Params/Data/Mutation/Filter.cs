using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class Filter : TrackingMutation
{
    private readonly EuroFilter[] _shapes;
    private readonly EuroFilter _gazeLeftX = new();
    private readonly EuroFilter _gazeLeftY = new();
    private readonly EuroFilter _gazeRightX = new();
    private readonly EuroFilter _gazeRightY = new();
    private readonly EuroFilter _pupilLeft = new();
    private readonly EuroFilter _pupilRight = new();
    private readonly EuroFilter _opennessLeft = new();
    private readonly EuroFilter _opennessRight = new();
    private readonly EuroFilter _headYaw = new();
    private readonly EuroFilter _headPitch = new();
    private readonly EuroFilter _headRoll = new();
    private readonly EuroFilter _headPosX = new();
    private readonly EuroFilter _headPosY = new();
    private readonly EuroFilter _headPosZ = new();

    public Filter()
    {
        _shapes = new EuroFilter[(int)UnifiedExpressions.Max];
        for (int i = 0; i < _shapes.Length; i++)
        {
            _shapes[i] = new EuroFilter();
        }
    }

    [MutationProperty("Minimum Cutoff", true, 0f, 2f, Developer = true)]
    public static float MinCutoff { get; set; } = 1f;

    [MutationProperty("Beta", true, Developer = true)]
    public static float Beta { get; set; } = 0.5f;

    [MutationProperty("Derivative Cutoff", true, Developer = true)]
    public static float DerivativeCutoff { get; set; } = 0.1f;

    public override string Name => "Data Filter";
    public override string Description => "Default data filtering for VRCFaceTracking expressions.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int Order => 100;
    public override int PageOrder => 5;
    public override bool IsSaved => true;

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
