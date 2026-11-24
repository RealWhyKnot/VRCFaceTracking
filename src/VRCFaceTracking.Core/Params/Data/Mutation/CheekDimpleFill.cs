using Newtonsoft.Json;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

[MutationCard(TrackingCard.FillIns)]
public sealed class CheekDimpleFill : TrackingMutation
{
    private static readonly string[] CheekSquintParameters = ["v2/CheekSquint", "CheeksSquint", "CheekSquintLeft", "CheekSquintRight"];
    private static readonly string[] DimpleParameters = ["v2/MouthDimple", "MouthDimple", "MouthDimpleLeft", "MouthDimpleRight"];

    public override string Name => "Cheek & Dimple Fill-In";
    public override string Description => "Shows cheek squint and dimples through the eye squint and smile on avatars that have no parameters for them.";
    public override MutationPriority Priority => MutationPriority.Postprocessor;
    public override int Order => 5;
    public override int PageOrder => 2;
    public override bool IsActive { get; set; }

    [MutationProperty("Cheek Squint to Eye Squint", true, Group = "Cheeks", Order = 10)]
    public bool CheekToEyeSquint { get; set; } = true;
    [MutationProperty("Cheek Squint Strength", true, Group = "Cheeks", Indented = true, Order = 11, Advanced = true, DependsOn = nameof(CheekToEyeSquint), Step = 0.05f)]
    public float CheekStrength { get; set; } = 0.5f;

    [MutationProperty("Dimple to Smile", true, Group = "Mouth", Order = 20)]
    public bool DimpleToSmile { get; set; } = true;
    [MutationProperty("Dimple Strength", true, Group = "Mouth", Indented = true, Order = 21, Advanced = true, DependsOn = nameof(DimpleToSmile), Step = 0.05f)]
    public float DimpleStrength { get; set; } = 1f;

    internal Func<AvatarParameterSnapshot?> CurrentAvatar { get; set; } = () => AvatarParameters.Current;

    [JsonIgnore]
    public FillInState FillState
    {
        get
        {
            if (!IsActive || (!CheekToEyeSquint && !DimpleToSmile))
            {
                return FillInState.Off;
            }

            AvatarParameterSnapshot? avatar = CurrentAvatar();
            if (avatar == null)
            {
                return FillInState.WaitingForAvatar;
            }

            if (avatar.UsesNativeFaceTracking)
            {
                return FillInState.NativeFaceTracking;
            }

            bool cheek = CheekToEyeSquint && !avatar.DrivesAny(CheekSquintParameters);
            bool dimple = DimpleToSmile && !avatar.DrivesAny(DimpleParameters);
            return cheek || dimple ? FillInState.Filling : FillInState.NotNeeded;
        }
    }

    [JsonIgnore]
    public override string StatusSummary => FillState switch
    {
        FillInState.WaitingForAvatar => "Waiting for avatar",
        FillInState.NativeFaceTracking => "Skipped: VRChat native face tracking active",
        FillInState.NotNeeded => "Skipped: avatar already has Cheek & Dimple parameters",
        FillInState.Filling => "Active",
        FillInState.Off => "Off",
        _ => "Off",
    };

    private const int CheekFeature = 0;
    private const int DimpleFeature = 1;

    protected override int FeatureOf(string propertyName) => propertyName switch
    {
        nameof(CheekToEyeSquint) => CheekFeature,
        nameof(DimpleToSmile) => DimpleFeature,
        _ => -1,
    };

    public override void MutateData(ref UnifiedTrackingData data)
    {
        AvatarParameterSnapshot? avatar = CurrentAvatar();
        if (avatar == null || avatar.UsesNativeFaceTracking)
        {
            return;
        }

        if (CheekToEyeSquint && !avatar.DrivesAny(CheekSquintParameters))
        {
            Fill(data, UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.EyeSquintLeft, CheekStrength, CheekFeature);
            Fill(data, UnifiedExpressions.CheekSquintRight, UnifiedExpressions.EyeSquintRight, CheekStrength, CheekFeature);
        }

        if (DimpleToSmile && !avatar.DrivesAny(DimpleParameters))
        {
            Fill(data, UnifiedExpressions.MouthDimpleLeft, UnifiedExpressions.MouthCornerPullLeft, DimpleStrength, DimpleFeature);
            Fill(data, UnifiedExpressions.MouthDimpleRight, UnifiedExpressions.MouthCornerPullRight, DimpleStrength, DimpleFeature);
        }
    }

    private void Fill(UnifiedTrackingData data, UnifiedExpressions source, UnifiedExpressions target, float strength, int feature)
    {
        float routed = Math.Clamp(data.Shapes[(int)source].Weight * strength, 0f, 1f);
        if (routed > data.Shapes[(int)target].Weight)
        {
            data.Shapes[(int)target].Weight = routed;
            MarkTriggered(feature);
        }
    }
}
