using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class ParameterAdjustment : TrackingMutation
{
    private static readonly (Func<ParameterAdjustment, (float Floor, float Ceiling)> Range, UnifiedExpressions[] Shapes)[] ShapeRanges =
    [
        (a => a.EyeBrowRaiser, [UnifiedExpressions.BrowInnerUpLeft, UnifiedExpressions.BrowInnerUpRight, UnifiedExpressions.BrowOuterUpLeft, UnifiedExpressions.BrowOuterUpRight]),
        (a => a.EyeBrowLower, [UnifiedExpressions.BrowLowererLeft, UnifiedExpressions.BrowLowererRight, UnifiedExpressions.BrowPinchLeft, UnifiedExpressions.BrowPinchRight]),
        (a => a.CheekPuffSuck, [UnifiedExpressions.CheekPuffLeft, UnifiedExpressions.CheekPuffRight, UnifiedExpressions.CheekSuckLeft, UnifiedExpressions.CheekSuckRight]),
        (a => a.CheekSquint, [UnifiedExpressions.CheekSquintLeft, UnifiedExpressions.CheekSquintRight]),
        (a => a.EyeSquint, [UnifiedExpressions.EyeSquintLeft, UnifiedExpressions.EyeSquintRight]),
        (a => a.EyeWide, [UnifiedExpressions.EyeWideLeft, UnifiedExpressions.EyeWideRight]),
        (a => a.JawZ, [UnifiedExpressions.JawBackward, UnifiedExpressions.JawForward]),
        (a => a.JawOpen, [UnifiedExpressions.JawClench, UnifiedExpressions.JawMandibleRaise, UnifiedExpressions.JawOpen]),
        (a => a.MouthClosed, [UnifiedExpressions.MouthClosed]),
        (a => a.JawX, [UnifiedExpressions.JawLeft, UnifiedExpressions.JawRight]),
        (a => a.LipFunnel, [UnifiedExpressions.LipFunnelLowerLeft, UnifiedExpressions.LipFunnelLowerRight, UnifiedExpressions.LipFunnelUpperLeft, UnifiedExpressions.LipFunnelUpperRight]),
        (a => a.LipPucker, [UnifiedExpressions.LipPuckerLowerLeft, UnifiedExpressions.LipPuckerLowerRight, UnifiedExpressions.LipPuckerUpperLeft, UnifiedExpressions.LipPuckerUpperRight]),
        (a => a.LipSuck, [UnifiedExpressions.LipSuckCornerLeft, UnifiedExpressions.LipSuckCornerRight, UnifiedExpressions.LipSuckUpperLeft, UnifiedExpressions.LipSuckUpperRight, UnifiedExpressions.LipSuckLowerLeft, UnifiedExpressions.LipSuckLowerRight]),
        (a => a.MouthOpener, [UnifiedExpressions.MouthUpperDeepenLeft, UnifiedExpressions.MouthUpperDeepenRight, UnifiedExpressions.MouthUpperUpLeft, UnifiedExpressions.MouthUpperUpRight, UnifiedExpressions.MouthLowerDownLeft, UnifiedExpressions.MouthLowerDownRight]),
        (a => a.MouthSmile, [UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.MouthCornerPullRight, UnifiedExpressions.MouthCornerSlantLeft, UnifiedExpressions.MouthCornerSlantRight]),
        (a => a.MouthFrown, [UnifiedExpressions.MouthFrownLeft, UnifiedExpressions.MouthFrownRight]),
        (a => a.MouthStretch, [UnifiedExpressions.MouthStretchLeft, UnifiedExpressions.MouthStretchRight]),
        (a => a.MouthTightener, [UnifiedExpressions.MouthTightenerLeft, UnifiedExpressions.MouthTightenerRight]),
        (a => a.MouthPress, [UnifiedExpressions.MouthPressRight, UnifiedExpressions.MouthPressLeft]),
        (a => a.MouthX, [UnifiedExpressions.MouthUpperLeft, UnifiedExpressions.MouthUpperRight, UnifiedExpressions.MouthLowerLeft, UnifiedExpressions.MouthLowerRight]),
        (a => a.MouthRaiser, [UnifiedExpressions.MouthRaiserLower, UnifiedExpressions.MouthRaiserUpper]),
        (a => a.Nose, [UnifiedExpressions.NasalConstrictLeft, UnifiedExpressions.NasalConstrictRight, UnifiedExpressions.NasalDilationLeft, UnifiedExpressions.NasalDilationRight]),
        (a => a.NoseSneer, [UnifiedExpressions.NoseSneerLeft, UnifiedExpressions.NoseSneerRight]),
        (a => a.Neck, [UnifiedExpressions.NeckFlexLeft, UnifiedExpressions.NeckFlexRight, UnifiedExpressions.SoftPalateClose, UnifiedExpressions.ThroatSwallow]),
        (a => a.TongueMove, [UnifiedExpressions.TongueBendDown, UnifiedExpressions.TongueCurlUp, UnifiedExpressions.TongueDown, UnifiedExpressions.TongueUp, UnifiedExpressions.TongueLeft, UnifiedExpressions.TongueRight]),
        (a => a.TongueOther, [UnifiedExpressions.TongueTwistLeft, UnifiedExpressions.TongueTwistRight, UnifiedExpressions.TongueFlat, UnifiedExpressions.TongueSquish, UnifiedExpressions.TongueRoll]),
        (a => a.TongueOut, [UnifiedExpressions.TongueOut]),
    ];

    [MutationProperty("Eyebrow Raiser", true, Group = "Brows and eyes")] public (float, float) EyeBrowRaiser { get; set; } = new(0, 1);
    [MutationProperty("Eyebrow Lowerer", true, Group = "Brows and eyes")] public (float, float) EyeBrowLower { get; set; } = new(0, 1);
    [MutationProperty("Eye Squint", true, Group = "Brows and eyes")] public (float, float) EyeSquint { get; set; } = new(0, 1);
    [MutationProperty("Eye Wide", true, Group = "Brows and eyes")] public (float, float) EyeWide { get; set; } = new(0, 1);
    [MutationProperty("Cheek", true, Group = "Cheeks")] public (float, float) CheekPuffSuck { get; set; } = new(0, 1);
    [MutationProperty("Cheek Squint", true, Group = "Cheeks")] public (float, float) CheekSquint { get; set; } = new(0, 1);
    [MutationProperty("Jaw", true, Group = "Jaw")] public (float, float) JawOpen { get; set; } = new(0, 1);
    [MutationProperty("MouthClosed", true, Group = "Jaw")] public (float, float) MouthClosed { get; set; } = new(0, 1);
    [MutationProperty("Jaw Sideways", true, Group = "Jaw")] public (float, float) JawX { get; set; } = new(0, 1);
    [MutationProperty("Jaw Forward / Backward", true, Group = "Jaw")] public (float, float) JawZ { get; set; } = new(0, 1);
    [MutationProperty("Lip Funnel", true, Group = "Lips")] public (float, float) LipFunnel { get; set; } = new(0, 1);
    [MutationProperty("Lip Suck", true, Group = "Lips")] public (float, float) LipSuck { get; set; } = new(0, 1);
    [MutationProperty("Lip Pucker", true, Group = "Lips")] public (float, float) LipPucker { get; set; } = new(0, 1);
    [MutationProperty("Mouth Open", true, Group = "Mouth")] public (float, float) MouthOpener { get; set; } = new(0, 1);
    [MutationProperty("Mouth Smile", true, Group = "Mouth")] public (float, float) MouthSmile { get; set; } = new(0, 1);
    [MutationProperty("Mouth Frown", true, Group = "Mouth")] public (float, float) MouthFrown { get; set; } = new(0, 1);
    [MutationProperty("Mouth Stretch", true, Group = "Mouth")] public (float, float) MouthStretch { get; set; } = new(0, 1);
    [MutationProperty("Mouth Tightener", true, Group = "Mouth")] public (float, float) MouthTightener { get; set; } = new(0, 1);
    [MutationProperty("Mouth Press", true, Group = "Mouth")] public (float, float) MouthPress { get; set; } = new(0, 1);
    [MutationProperty("Mouth Sideways", true, Group = "Mouth")] public (float, float) MouthX { get; set; } = new(0, 1);
    [MutationProperty("Mouth Raiser", true, Group = "Mouth")] public (float, float) MouthRaiser { get; set; } = new(0, 1);
    [MutationProperty("Nose", true, Group = "Nose")] public (float, float) Nose { get; set; } = new(0, 1);
    [MutationProperty("Nose Sneer", true, Group = "Nose")] public (float, float) NoseSneer { get; set; } = new(0, 1);
    [MutationProperty("Tongue Out", true, Group = "Tongue")] public (float, float) TongueOut { get; set; } = new(0, 1);
    [MutationProperty("Tongue Directions", true, Group = "Tongue")] public (float, float) TongueMove { get; set; } = new(0, 1);
    [MutationProperty("Tongue Miscellaneous", true, Group = "Tongue")] public (float, float) TongueOther { get; set; } = new(0, 1);

    [MutationProperty("Neck", true, Group = "Head and neck")] public (float, float) Neck { get; set; } = new(0, 1);
    [MutationProperty("Head Rotation (Side-to-Side)", true, Group = "Head and neck")] public (float, float) HeadRotationYaw { get; set; } = new(-1, 1);
    [MutationProperty("Head Rotation (Up-Down Tilt)", true, Group = "Head and neck")] public (float, float) HeadRotationPitch { get; set; } = new(-1, 1);
    [MutationProperty("Head Rotation (Side Tilt)", true, Group = "Head and neck")] public (float, float) HeadRotationRoll { get; set; } = new(-1, 1);
    [MutationProperty("Head Position (Side-to-Side)", true, Group = "Head and neck")] public (float, float) HeadPositionX { get; set; } = new(-1, 1);
    [MutationProperty("Head Position (Up-Down)", true, Group = "Head and neck")] public (float, float) HeadPositionY { get; set; } = new(-1, 1);
    [MutationProperty("Head Position (Forward-Back)", true, Group = "Head and neck")] public (float, float) HeadPositionZ { get; set; } = new(-1, 1);

    public override string Name => "Parameter Adjustment";
    public override string Description => "Adjust VRCFaceTracking Parameters.";
    public override MutationPriority Priority => MutationPriority.None;
    public override int PageOrder => 2;

    [MutationButton("Reset Values")]
    public static void Reset()
    {
    }

    public override void MutateData(ref UnifiedTrackingData data)
    {
        foreach ((Func<ParameterAdjustment, (float Floor, float Ceiling)> range, UnifiedExpressions[] shapes) in ShapeRanges)
        {
            (float floor, float ceiling) = range(this);
            foreach (UnifiedExpressions shape in shapes)
            {
                ref float weight = ref data.Shapes[(int)shape].Weight;
                weight = Rescale(weight, floor, ceiling);
            }
        }

        data.Head.HeadYaw = Rescale(data.Head.HeadYaw, HeadRotationYaw.Item1, HeadRotationYaw.Item2);
        data.Head.HeadPitch = Rescale(data.Head.HeadPitch, HeadRotationPitch.Item1, HeadRotationPitch.Item2);
        data.Head.HeadRoll = Rescale(data.Head.HeadRoll, HeadRotationRoll.Item1, HeadRotationRoll.Item2);
        data.Head.HeadPosX = Rescale(data.Head.HeadPosX, HeadPositionX.Item1, HeadPositionX.Item2);
        data.Head.HeadPosY = Rescale(data.Head.HeadPosY, HeadPositionY.Item1, HeadPositionY.Item2);
        data.Head.HeadPosZ = Rescale(data.Head.HeadPosZ, HeadPositionZ.Item1, HeadPositionZ.Item2);
    }

    private static float Rescale(float value, float floor, float ceiling) => (value - floor) / (ceiling - floor);
}
