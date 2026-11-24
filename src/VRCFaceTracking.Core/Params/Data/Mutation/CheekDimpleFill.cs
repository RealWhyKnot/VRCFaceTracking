using Newtonsoft.Json;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

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

    [MutationProperty("Cheek Squint to Eye Squint", true, Group = "Cheeks")]
    public bool CheekToEyeSquint { get; set; } = true;
    [MutationProperty("Cheek Squint Strength", true, Group = "Cheeks", Indented = true)]
    public float CheekStrength { get; set; } = 0.5f;

    [MutationProperty("Dimple to Smile", true, Group = "Mouth")]
    public bool DimpleToSmile { get; set; } = true;
    [MutationProperty("Dimple Strength", true, Group = "Mouth", Indented = true)]
    public float DimpleStrength { get; set; } = 1f;

    internal Func<AvatarParameterSnapshot?> CurrentAvatar { get; set; } = () => AvatarParameters.Current;

    [JsonIgnore]
    public override string StatusSummary
    {
        get
        {
            if (!IsActive || (!CheekToEyeSquint && !DimpleToSmile))
            {
                return "Off";
            }

            AvatarParameterSnapshot? avatar = CurrentAvatar();
            if (avatar == null)
            {
                return "Waiting for avatar";
            }

            if (avatar.UsesNativeFaceTracking)
            {
                return "Skipped: VRChat native face tracking active";
            }

            bool cheek = CheekToEyeSquint && !avatar.DrivesAny(CheekSquintParameters);
            bool dimple = DimpleToSmile && !avatar.DrivesAny(DimpleParameters);
            return cheek || dimple ? "Active" : "Skipped: avatar already has Cheek & Dimple parameters";
        }
    }

    public override void MutateData(ref UnifiedTrackingData data)
    {
        AvatarParameterSnapshot? avatar = CurrentAvatar();
        if (avatar == null || avatar.UsesNativeFaceTracking)
        {
            return;
        }

        if (CheekToEyeSquint && !avatar.DrivesAny(CheekSquintParameters))
        {
            Fill(data, UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.EyeSquintLeft, CheekStrength);
            Fill(data, UnifiedExpressions.CheekSquintRight, UnifiedExpressions.EyeSquintRight, CheekStrength);
        }

        if (DimpleToSmile && !avatar.DrivesAny(DimpleParameters))
        {
            Fill(data, UnifiedExpressions.MouthDimpleLeft, UnifiedExpressions.MouthCornerPullLeft, DimpleStrength);
            Fill(data, UnifiedExpressions.MouthDimpleRight, UnifiedExpressions.MouthCornerPullRight, DimpleStrength);
        }
    }

    private void Fill(UnifiedTrackingData data, UnifiedExpressions source, UnifiedExpressions target, float strength)
    {
        float routed = Math.Clamp(data.Shapes[(int)source].Weight * strength, 0f, 1f);
        if (routed > data.Shapes[(int)target].Weight)
        {
            data.Shapes[(int)target].Weight = routed;
            MarkTriggered();
        }
    }
}
