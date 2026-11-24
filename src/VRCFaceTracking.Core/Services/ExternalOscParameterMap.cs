using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Services;

public static class ExternalOscParameterMap
{
    private const string AvatarPrefix = "/avatar/parameters/";

    private static readonly string[] VersionPrefixes = ["ft/", "v2/"];

    private static readonly Dictionary<string, UnifiedExpressions[]> Shapes = BuildShapes();

    private static readonly Dictionary<string, ExternalOscEyeChannel> EyeChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftEyeX"] = ExternalOscEyeChannel.LeftX,
        ["EyeLeftX"] = ExternalOscEyeChannel.LeftX,
        ["LeftEyeY"] = ExternalOscEyeChannel.LeftY,
        ["EyeLeftY"] = ExternalOscEyeChannel.LeftY,
        ["RightEyeX"] = ExternalOscEyeChannel.RightX,
        ["EyeRightX"] = ExternalOscEyeChannel.RightX,
        ["RightEyeY"] = ExternalOscEyeChannel.RightY,
        ["EyeRightY"] = ExternalOscEyeChannel.RightY,
        ["LeftEyeLid"] = ExternalOscEyeChannel.LeftOpenness,
        ["RightEyeLid"] = ExternalOscEyeChannel.RightOpenness,
        ["EyeX"] = ExternalOscEyeChannel.CombinedX,
        ["EyeY"] = ExternalOscEyeChannel.CombinedY,
        ["EyesY"] = ExternalOscEyeChannel.CombinedY,
        ["EyesX"] = ExternalOscEyeChannel.CombinedX,
        ["EyeOpenLeft"] = ExternalOscEyeChannel.LeftOpenness,
        ["EyeOpenRight"] = ExternalOscEyeChannel.RightOpenness,
        ["EyeOpen"] = ExternalOscEyeChannel.CombinedOpenness,
        ["CombinedEyeLid"] = ExternalOscEyeChannel.CombinedOpenness,
    };

    private static readonly Dictionary<string, ExternalOscLid> Lids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EyeClosedLeft"] = new(ExternalOscLidSide.Left, ExternalOscLidFormat.Closed),
        ["EyeClosedRight"] = new(ExternalOscLidSide.Right, ExternalOscLidFormat.Closed),
        ["EyeClosed"] = new(ExternalOscLidSide.Both, ExternalOscLidFormat.Closed),
        ["EyeLidLeft"] = new(ExternalOscLidSide.Left, ExternalOscLidFormat.Unified),
        ["EyeLidRight"] = new(ExternalOscLidSide.Right, ExternalOscLidFormat.Unified),
        ["EyeLid"] = new(ExternalOscLidSide.Both, ExternalOscLidFormat.Unified),
        ["LeftEyeLidExpanded"] = new(ExternalOscLidSide.Left, ExternalOscLidFormat.Expanded),
        ["RightEyeLidExpanded"] = new(ExternalOscLidSide.Right, ExternalOscLidFormat.Expanded),
        ["CombinedEyeLidExpanded"] = new(ExternalOscLidSide.Both, ExternalOscLidFormat.Expanded),
        ["EyeLidExpanded"] = new(ExternalOscLidSide.Both, ExternalOscLidFormat.Expanded),
        ["LeftEyeLidExpandedSqueeze"] = new(ExternalOscLidSide.Left, ExternalOscLidFormat.ExpandedSqueeze),
        ["RightEyeLidExpandedSqueeze"] = new(ExternalOscLidSide.Right, ExternalOscLidFormat.ExpandedSqueeze),
        ["CombinedEyeLidExpandedSqueeze"] = new(ExternalOscLidSide.Both, ExternalOscLidFormat.ExpandedSqueeze),
        ["EyeLidExpandedSqueeze"] = new(ExternalOscLidSide.Both, ExternalOscLidFormat.ExpandedSqueeze),
    };

    public static ExternalOscTarget? Resolve(string address)
    {
        if (string.IsNullOrEmpty(address) || address[0] != '/')
        {
            return null;
        }

        string rest = address.StartsWith(AvatarPrefix, StringComparison.OrdinalIgnoreCase)
            ? address[AvatarPrefix.Length..]
            : address[1..];
        foreach (string prefix in VersionPrefixes)
        {
            if (rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest[prefix.Length..];
            }
        }

        return EyeChannels.TryGetValue(rest, out ExternalOscEyeChannel channel)
            ? new ExternalOscTarget([], channel)
            : Lids.TryGetValue(rest, out ExternalOscLid lid)
                ? new ExternalOscTarget([], EyeChannel: null, lid)
                : Shapes.TryGetValue(rest, out UnifiedExpressions[]? shapes) ? new ExternalOscTarget(shapes, EyeChannel: null) : null;
    }

    private static Dictionary<string, UnifiedExpressions[]> BuildShapes()
    {
        Dictionary<string, UnifiedExpressions[]> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (UnifiedExpressions expression in Enum.GetValues<UnifiedExpressions>())
        {
            if (expression != UnifiedExpressions.Max)
            {
                map[expression.ToString()] = [expression];
            }
        }

        map["mouthClose"] = [UnifiedExpressions.MouthClosed];
        map["mouthFunnel"] = [UnifiedExpressions.LipFunnelUpperRight, UnifiedExpressions.LipFunnelUpperLeft, UnifiedExpressions.LipFunnelLowerRight, UnifiedExpressions.LipFunnelLowerLeft];
        map["mouthPucker"] = [UnifiedExpressions.LipPuckerUpperRight, UnifiedExpressions.LipPuckerUpperLeft, UnifiedExpressions.LipPuckerLowerRight, UnifiedExpressions.LipPuckerLowerLeft];
        map["mouthRollUpper"] = [UnifiedExpressions.LipSuckUpperRight, UnifiedExpressions.LipSuckUpperLeft];
        map["mouthRollLower"] = [UnifiedExpressions.LipSuckLowerRight, UnifiedExpressions.LipSuckLowerLeft];
        map["mouthShrugUpper"] = [UnifiedExpressions.MouthRaiserUpper];
        map["mouthShrugLower"] = [UnifiedExpressions.MouthRaiserLower];
        map["mouthLeft"] = [UnifiedExpressions.MouthUpperLeft, UnifiedExpressions.MouthLowerLeft];
        map["mouthRight"] = [UnifiedExpressions.MouthUpperRight, UnifiedExpressions.MouthLowerRight];
        map["mouthSmileLeft"] = [UnifiedExpressions.MouthCornerPullLeft, UnifiedExpressions.MouthCornerSlantLeft];
        map["mouthSmileRight"] = [UnifiedExpressions.MouthCornerPullRight, UnifiedExpressions.MouthCornerSlantRight];
        map["browDownLeft"] = [UnifiedExpressions.BrowLowererLeft];
        map["browDownRight"] = [UnifiedExpressions.BrowLowererRight];
        map["browInnerUp"] = [UnifiedExpressions.BrowInnerUpLeft, UnifiedExpressions.BrowInnerUpRight];
        map["cheekPuff"] = [UnifiedExpressions.CheekPuffLeft, UnifiedExpressions.CheekPuffRight];
        map["cheekSuck"] = [UnifiedExpressions.CheekSuckLeft, UnifiedExpressions.CheekSuckRight];
        return map;
    }
}
