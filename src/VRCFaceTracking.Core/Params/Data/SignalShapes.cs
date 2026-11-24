using System.Globalization;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data;

internal static class SignalShapes
{
    private static readonly string[] SideSuffixes = ["Left", "Right"];

    private static readonly Dictionary<string, string[]> Derived = new(StringComparer.Ordinal)
    {
        ["JawRight"] = ["v2/JawX"],
        ["JawLeft"] = ["v2/JawX"],
        ["JawForward"] = ["v2/JawZ"],
        ["MouthSmile"] = [.. Sided("v2/SmileSad"), .. Sided("v2/SmileFrown")],
        ["MouthSad"] = Sided("v2/SmileSad"),
        ["MouthFrown"] = [.. Sided("v2/SmileFrown"), .. Sided("v2/MouthCornerY")],
        ["MouthCornerSlant"] = Sided("v2/MouthCornerY"),
        ["BrowLowerer"] = ["v2/BrowDown", .. Sided("v2/BrowExpression")],
        ["BrowPinch"] = ["v2/BrowDown", .. Sided("v2/BrowExpression")],
        ["BrowInnerUp"] = ["v2/BrowUp", .. Sided("v2/BrowExpression")],
        ["BrowOuterUp"] = ["v2/BrowUp", .. Sided("v2/BrowExpression")],
        ["CheekPuff"] = Sided("v2/CheekPuffSuck"),
        ["CheekSuck"] = Sided("v2/CheekPuffSuck"),
        ["MouthUpperUp"] = ["v2/MouthOpen"],
        ["MouthLowerDown"] = ["v2/MouthOpen"],
        ["MouthUpper"] = ["v2/MouthUpperX", "v2/MouthX"],
        ["MouthLower"] = ["v2/MouthLowerX", "v2/MouthX"],
        ["MouthStretch"] = Sided("v2/MouthTightenerStretch"),
        ["MouthTightener"] = Sided("v2/MouthTightenerStretch"),
        ["LipSuckUpper"] = ["v2/LipSuck", .. Sided("v2/LipSuckFunnelUpper")],
        ["LipSuckLower"] = ["v2/LipSuck", .. Sided("v2/LipSuckFunnelLower")],
        ["LipFunnelUpper"] = ["v2/LipFunnel", .. Sided("v2/LipSuckFunnelUpper")],
        ["LipFunnelLower"] = ["v2/LipFunnel", .. Sided("v2/LipSuckFunnelLower")],
        ["LipPuckerUpper"] = ["v2/LipPucker", "v2/LipPuckerLeft", "v2/LipPuckerRight"],
        ["LipPuckerLower"] = ["v2/LipPucker", "v2/LipPuckerLeft", "v2/LipPuckerRight"],
        ["EyeSquint"] = ["v2/EyeSquint", "v2/EyesSquint"],
        ["CheekSquint"] = ["CheeksSquint"],
        ["TongueRight"] = ["v2/TongueX"],
        ["TongueLeft"] = ["v2/TongueX"],
        ["TongueUp"] = ["v2/TongueY"],
        ["TongueDown"] = ["v2/TongueY"],
        ["TongueCurlUp"] = ["v2/TongueArchY"],
        ["TongueBendDown"] = ["v2/TongueArchY"],
        ["TongueFlat"] = ["v2/TongueShape"],
        ["TongueSquish"] = ["v2/TongueShape"],
    };

    private static readonly string[][] Names = BuildNames();

    private static readonly SignalGroup[] AllGroups = BuildGroups();

    public static IReadOnlyList<SignalGroup> Groups => AllGroups;

    public static string[] NamesOf(UnifiedExpressions shape) => Names[(int)shape];

    public static string[] NamesOf(UnifiedExpressions left, UnifiedExpressions right)
    {
        string[] first = Names[(int)left];
        string[] second = Names[(int)right];
        return [.. first.Union(second, StringComparer.Ordinal)];
    }

    public static string SignalName(UnifiedExpressions shape) => BaseOf(Enum.GetName(shape) ?? shape.ToString());

    private static string BaseOf(string name)
    {
        foreach (string suffix in SideSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    private static string[] Sided(string name) => [name, name + "Left", name + "Right"];

    private static string[][] BuildNames()
    {
        int count = (int)UnifiedExpressions.Max;
        string[][] result = new string[count][];
        for (int i = 0; i < count; i++)
        {
            string name = Enum.GetName((UnifiedExpressions)i) ?? i.ToString(CultureInfo.InvariantCulture);
            string signal = BaseOf(name);
            HashSet<string> set = new(StringComparer.Ordinal) { "v2/" + name, name, "v2/" + signal, signal };
            if (Derived.TryGetValue(signal, out string[]? extra))
            {
                set.UnionWith(extra);
            }

            if (Derived.TryGetValue(name, out string[]? exact))
            {
                set.UnionWith(exact);
            }

            result[i] = [.. set];
        }

        return result;
    }

    private static SignalGroup[] BuildGroups()
    {
        int count = (int)UnifiedExpressions.Max;
        List<SignalGroup> groups = [];
        for (int i = 0; i < count; i++)
        {
            var shape = (UnifiedExpressions)i;
            string name = Enum.GetName(shape) ?? string.Empty;
            if (name.EndsWith("Right", StringComparison.Ordinal) && Enum.TryParse(string.Concat(name.AsSpan(0, name.Length - 5), "Left"), out UnifiedExpressions left) && (int)left < count)
            {
                continue;
            }

            if (name.EndsWith("Left", StringComparison.Ordinal) && Enum.TryParse(string.Concat(name.AsSpan(0, name.Length - 4), "Right"), out UnifiedExpressions right) && (int)right < count)
            {
                groups.Add(new SignalGroup(shape, right, BaseOf(name)));
                continue;
            }

            groups.Add(new SignalGroup(shape, shape, name));
        }

        return [.. groups];
    }
}
