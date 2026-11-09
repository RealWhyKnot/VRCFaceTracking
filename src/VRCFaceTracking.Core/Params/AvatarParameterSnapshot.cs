namespace VRCFaceTracking.Core.Params;

internal sealed class AvatarParameterSnapshot
{
    private const string NegativeSuffix = "Negative";

    private readonly HashSet<string> _baseNames;

    public AvatarParameterSnapshot(bool usesNativeFaceTracking, IEnumerable<Parameter> relevant)
        : this(usesNativeFaceTracking, relevant.SelectMany(parameter => parameter.GetParamNames()).Select(name => name.paramName))
    {
    }

    public AvatarParameterSnapshot(bool usesNativeFaceTracking, IEnumerable<string> parameterNames)
    {
        UsesNativeFaceTracking = usesNativeFaceTracking;
        _baseNames = new HashSet<string>(parameterNames.Select(BaseName), StringComparer.Ordinal);
    }

    public bool UsesNativeFaceTracking { get; }

    public bool DrivesAny(IEnumerable<string> names) => names.Any(_baseNames.Contains);

    private static string BaseName(string name)
    {
        ReadOnlySpan<char> span = name;
        if (span.EndsWith(NegativeSuffix, StringComparison.Ordinal))
        {
            span = span[..^NegativeSuffix.Length];
        }

        return span.TrimEnd("0123456789").ToString();
    }
}
