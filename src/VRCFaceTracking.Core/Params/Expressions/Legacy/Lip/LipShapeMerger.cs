using VRCFaceTracking.Core.Params.DataTypes;

namespace VRCFaceTracking.Core.Params.Expressions.Legacy.Lip;

public static class LipShapeMerger
{
    private static readonly Dictionary<string, ICombinedShape> MergedShapes =
        new(StringComparer.Ordinal)
        {
            {"JawX", new PositiveNegativeShape(SRanipal_LipShape_v2.JawRight, SRanipal_LipShape_v2.JawLeft)},
            {"MouthUpper", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperRight, SRanipal_LipShape_v2.MouthUpperLeft)},
            {"MouthLower", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerRight, SRanipal_LipShape_v2.MouthLowerLeft)},
            {"MouthX", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperRight, SRanipal_LipShape_v2.MouthLowerRight], [SRanipal_LipShape_v2.MouthUpperLeft, SRanipal_LipShape_v2.MouthLowerLeft], useMax: true)},
            {"SmileSadRight", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileRight, SRanipal_LipShape_v2.MouthSadRight)},
            {"SmileSadLeft", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSadLeft)},
            {"SmileSad", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthSadLeft, SRanipal_LipShape_v2.MouthSadRight])},
            {"TongueY", new PositiveNegativeShape(SRanipal_LipShape_v2.TongueUp, SRanipal_LipShape_v2.TongueDown)},
            {"TongueX", new PositiveNegativeShape(SRanipal_LipShape_v2.TongueRight, SRanipal_LipShape_v2.TongueLeft)},
            {"PuffSuckRight", new PositiveNegativeShape(SRanipal_LipShape_v2.CheekPuffRight, SRanipal_LipShape_v2.CheekSuck)},
            {"PuffSuckLeft", new PositiveNegativeShape(SRanipal_LipShape_v2.CheekPuffLeft, SRanipal_LipShape_v2.CheekSuck)},
            {"PuffSuck", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.CheekPuffLeft, SRanipal_LipShape_v2.CheekPuffRight], [SRanipal_LipShape_v2.CheekSuck], useMax: true)},

            {"JawOpenApe", new PositiveNegativeShape(SRanipal_LipShape_v2.JawOpen, SRanipal_LipShape_v2.MouthApeShape)},
            {"JawOpenPuff", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.JawOpen], [SRanipal_LipShape_v2.CheekPuffLeft, SRanipal_LipShape_v2.CheekPuffRight])},
            {"JawOpenPuffRight", new PositiveNegativeShape(SRanipal_LipShape_v2.JawOpen, SRanipal_LipShape_v2.CheekPuffRight)},
            {"JawOpenPuffLeft", new PositiveNegativeShape(SRanipal_LipShape_v2.JawOpen, SRanipal_LipShape_v2.CheekPuffLeft)},
            {"JawOpenSuck", new PositiveNegativeShape(SRanipal_LipShape_v2.JawOpen, SRanipal_LipShape_v2.CheekSuck)},
            {"JawOpenForward", new PositiveNegativeShape(SRanipal_LipShape_v2.JawOpen, SRanipal_LipShape_v2.JawForward)},
            {"JawOpenOverlay", new PositiveNegativeShape(SRanipal_LipShape_v2.JawOpen, SRanipal_LipShape_v2.MouthLowerOverlay)},

            {"MouthUpperUpRightUpperInside", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpRight, SRanipal_LipShape_v2.MouthUpperInside)},
            {"MouthUpperUpRightPuffRight", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpRight, SRanipal_LipShape_v2.CheekPuffRight)},
            {"MouthUpperUpRightApe", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpRight, SRanipal_LipShape_v2.MouthApeShape)},
            {"MouthUpperUpRightPout", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpRight, SRanipal_LipShape_v2.MouthPout)},
            {"MouthUpperUpRightOverlay", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpRight, SRanipal_LipShape_v2.MouthLowerOverlay)},
            {"MouthUpperUpRightSuck", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpRight, SRanipal_LipShape_v2.CheekSuck)},

            {"MouthUpperUpLeftUpperInside", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperInside)},
            {"MouthUpperUpLeftPuffLeft", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.CheekPuffLeft)},
            {"MouthUpperUpLeftApe", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthApeShape)},
            {"MouthUpperUpLeftPout", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthPout)},
            {"MouthUpperUpLeftOverlay", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthLowerOverlay)},
            {"MouthUpperUpLeftSuck", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.CheekSuck)},

            {"MouthUpperUpUpperInside", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.MouthUpperInside])},
            {"MouthUpperUpInside", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.MouthUpperInside, SRanipal_LipShape_v2.MouthLowerInside], useMax: true)},
            {"MouthUpperUpPuff", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.CheekPuffLeft, SRanipal_LipShape_v2.CheekPuffRight])},
            {"MouthUpperUpPuffLeft", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.CheekPuffLeft])},
            {"MouthUpperUpPuffRight", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.CheekPuffRight])},
            {"MouthUpperUpApe", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.MouthApeShape])},
            {"MouthUpperUpPout", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.MouthPout])},
            {"MouthUpperUpOverlay", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.MouthLowerOverlay])},
            {"MouthUpperUpSuck", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthUpperUpLeft, SRanipal_LipShape_v2.MouthUpperUpRight], [SRanipal_LipShape_v2.CheekSuck])},

            {"MouthLowerDownRightLowerInside", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownRight, SRanipal_LipShape_v2.MouthLowerInside)},
            {"MouthLowerDownRightPuffRight", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownRight, SRanipal_LipShape_v2.CheekPuffRight)},
            {"MouthLowerDownRightApe", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownRight, SRanipal_LipShape_v2.MouthApeShape)},
            {"MouthLowerDownRightPout", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownRight, SRanipal_LipShape_v2.MouthPout)},
            {"MouthLowerDownRightOverlay", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownRight, SRanipal_LipShape_v2.MouthLowerOverlay)},
            {"MouthLowerDownRightSuck", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownRight, SRanipal_LipShape_v2.CheekSuck)},

            {"MouthLowerDownLeftLowerInside", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerInside)},
            {"MouthLowerDownLeftPuffLeft", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.CheekPuffLeft)},
            {"MouthLowerDownLeftApe", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthApeShape)},
            {"MouthLowerDownLeftPout", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthPout)},
            {"MouthLowerDownLeftOverlay", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerOverlay)},
            {"MouthLowerDownLeftSuck", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.CheekSuck)},

            {"MouthLowerDownLowerInside", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.MouthLowerInside])},
            {"MouthLowerDownInside", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.MouthUpperInside, SRanipal_LipShape_v2.MouthLowerInside], useMax: true)},
            {"MouthLowerDownPuff", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.CheekPuffLeft, SRanipal_LipShape_v2.CheekPuffRight])},
            {"MouthLowerDownPuffLeft", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.CheekPuffLeft])},
            {"MouthLowerDownPuffRight", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.CheekPuffRight])},
            {"MouthLowerDownApe", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.MouthApeShape])},
            {"MouthLowerDownPout", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.MouthPout])},
            {"MouthLowerDownOverlay", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.MouthLowerOverlay])},
            {"MouthLowerDownSuck", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthLowerDownLeft, SRanipal_LipShape_v2.MouthLowerDownRight], [SRanipal_LipShape_v2.CheekSuck])},

                {"MouthUpperInsideOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthUpperInside, SRanipal_LipShape_v2.MouthUpperOverturn)},
            {"MouthLowerInsideOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthLowerInside, SRanipal_LipShape_v2.MouthLowerOverturn)},

            {"SmileRightUpperOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileRight, SRanipal_LipShape_v2.MouthUpperOverturn)},
            {"SmileRightLowerOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileRight, SRanipal_LipShape_v2.MouthLowerOverturn)},
            {"SmileRightOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthUpperOverturn, SRanipal_LipShape_v2.MouthLowerOverturn])},
            {"SmileRightApe", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileRight, SRanipal_LipShape_v2.MouthApeShape)},
            {"SmileRightOverlay", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileRight, SRanipal_LipShape_v2.MouthLowerOverlay)},
            {"SmileRightPout", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileRight, SRanipal_LipShape_v2.MouthPout)},

            {"SmileLeftUpperOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthUpperOverturn)},
            {"SmileLeftLowerOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthLowerOverturn)},
            {"SmileLeftOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft], [SRanipal_LipShape_v2.MouthUpperOverturn, SRanipal_LipShape_v2.MouthLowerOverturn])},
            {"SmileLeftApe", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthApeShape)},
            {"SmileLeftOverlay", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthLowerOverlay)},
            {"SmileLeftPout", new PositiveNegativeShape(SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthPout)},

            {"SmileUpperOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthUpperOverturn])},
            {"SmileLowerOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthLowerOverturn])},
            {"SmileOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthUpperOverturn, SRanipal_LipShape_v2.MouthLowerOverturn])},
            {"SmileApe", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthApeShape])},
            {"SmileOverlay", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthLowerOverlay])},
            {"SmilePout", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.MouthSmileLeft, SRanipal_LipShape_v2.MouthSmileRight], [SRanipal_LipShape_v2.MouthPout])},

            {"PuffRightUpperOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.CheekPuffRight, SRanipal_LipShape_v2.MouthUpperOverturn)},
            {"PuffRightLowerOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.CheekPuffRight, SRanipal_LipShape_v2.MouthLowerOverturn)},
            {"PuffRightOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.CheekPuffRight], [SRanipal_LipShape_v2.MouthUpperOverturn, SRanipal_LipShape_v2.MouthLowerOverturn], useMax: true)},

            {"PuffLeftUpperOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.CheekPuffLeft, SRanipal_LipShape_v2.MouthUpperOverturn)},
            {"PuffLeftLowerOverturn", new PositiveNegativeShape(SRanipal_LipShape_v2.CheekPuffLeft, SRanipal_LipShape_v2.MouthLowerOverturn)},
            {"PuffLeftOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.CheekPuffLeft], [SRanipal_LipShape_v2.MouthUpperOverturn, SRanipal_LipShape_v2.MouthLowerOverturn], useMax: true)},

            {"PuffUpperOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.CheekPuffRight, SRanipal_LipShape_v2.CheekPuffLeft], [SRanipal_LipShape_v2.MouthUpperOverturn])},
            {"PuffLowerOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.CheekPuffRight, SRanipal_LipShape_v2.CheekPuffLeft], [SRanipal_LipShape_v2.MouthLowerOverturn])},
            {"PuffOverturn", new PositiveNegativeAveragedShape([SRanipal_LipShape_v2.CheekPuffRight, SRanipal_LipShape_v2.CheekPuffLeft], [SRanipal_LipShape_v2.MouthUpperOverturn, SRanipal_LipShape_v2.MouthLowerOverturn], useMax: true)},

            {"TongueSteps", new PositiveNegativeShape(SRanipal_LipShape_v2.TongueLongStep1, SRanipal_LipShape_v2.TongueLongStep2, steps: true)},
        };

    public static readonly Parameter[] AllLipParameters =
        [.. GetAllLipShapes().Union(GetOptimizedLipParameters())];

    private static IEnumerable<EParam> GetOptimizedLipParameters() => MergedShapes
        .Select(shape => new EParam(shape.Key, exp =>
            shape.Value.GetBlendedLipShape(exp), 0.0f));

    private static IEnumerable<EParam> GetAllLipShapes() =>
        Enum.GetValues<SRanipal_LipShape_v2>().Select(shape =>
           new EParam(shape.ToString(), exp => UnifiedSRanMapper.GetTransformedShape(shape, exp), 0.0f));
}
