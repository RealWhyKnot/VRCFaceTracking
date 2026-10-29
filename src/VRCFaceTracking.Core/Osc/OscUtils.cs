namespace VRCFaceTracking.Core.Osc;

internal static class OscUtils
{
    public static readonly IReadOnlyDictionary<(Type, char[] typeChar), (OscValueType oscType, string configType)> TypeConversions =
        new Dictionary<(Type, char[] typeChar), (OscValueType oscType, string configType)>
        {
            {(typeof(bool), new[]{'T', 'F'}), (OscValueType.Bool, "Bool")},
            {(typeof(float), new[]{'f'}), (OscValueType.Float, "Float")},
            {(typeof(int), new[]{'i'}), (OscValueType.Int, "Int")},
            {(typeof(string), new[]{'s'}), (OscValueType.String, "String")},
        };
}
