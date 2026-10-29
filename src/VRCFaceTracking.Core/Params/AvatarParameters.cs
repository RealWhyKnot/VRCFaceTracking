using System.Text;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Osc.DataTypes;
using VRCFaceTracking.Core.Params.DataTypes;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Params;

internal static class AvatarParameters
{
    private const int MaxAddressBytes = 1024;

    private static readonly HashSet<Parameter> SentAlongsideNativeFaceTracking =
    [
        .. TrackingOutput.AllParameters.Where(parameter => parameter is ConditionalBoolParameter),
        .. TrackingOutput.HeadParameters,
    ];

    private static readonly Lazy<string[]> LeftToVrchatNames = new(() =>
        [.. TrackingOutput.AllParameters.Where(parameter => !SentAlongsideNativeFaceTracking.Contains(parameter))
            .SelectMany(parameter => parameter.GetParamNames())
            .Select(name => name.paramName)
            .Distinct(StringComparer.Ordinal)]);

    public static List<Parameter> Reset(IReadOnlyList<IParameterDefinition> offeredDefinitions, bool fullFaceTracking, List<string> leftToVrchat)
    {
        IReadOnlyList<IParameterDefinition> definitions = [.. offeredDefinitions.Where(IsSendable)];
        var relevant = new List<Parameter>();
        bool gate = fullFaceTracking && !ParameterSenderService.AllParametersRelevantStatic;
        foreach (Parameter parameter in TrackingOutput.AllParameters)
        {
            IReadOnlyList<IParameterDefinition> offered = !gate || SentAlongsideNativeFaceTracking.Contains(parameter)
                ? definitions
                : [];
            relevant.AddRange(parameter.ResetParam(offered));
        }

        if (gate)
        {
            leftToVrchat.AddRange(definitions.Where(IsLeftToVrchat).Select(definition => definition.Name).Distinct(StringComparer.Ordinal));
        }

        return relevant;
    }

    private static bool IsSendable(IParameterDefinition definition) =>
        definition.Address is ['/', ..] address && Encoding.UTF8.GetByteCount(address) <= MaxAddressBytes;

    private static bool IsLeftToVrchat(IParameterDefinition definition) =>
        LeftToVrchatNames.Value.Any(name =>
            ParamAddressMatcher.Matches(definition.Address, name)
            || ParamAddressMatcher.MatchesIndexed(definition.Address, name));
}
