using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.OSC.DataTypes;
using VRCFaceTracking.Core.Params.DataTypes;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Params;

public static class AvatarParameters
{
    private static readonly HashSet<Parameter> SentAlongsideNativeFaceTracking =
        UnifiedTracking.AllParameters.Where(parameter => parameter is ConditionalBoolParameter)
            .Concat(UnifiedTracking.HeadParameters)
            .ToHashSet();

    private static readonly Lazy<string[]> LeftToVrchatNames = new(() =>
        UnifiedTracking.AllParameters.Where(parameter => !SentAlongsideNativeFaceTracking.Contains(parameter))
            .SelectMany(parameter => parameter.GetParamNames())
            .Select(name => name.paramName)
            .Distinct()
            .ToArray());

    public static List<Parameter> Reset(IParameterDefinition[] definitions, bool fullFaceTracking, List<string> leftToVrchat)
    {
        var relevant = new List<Parameter>();
        var gate = fullFaceTracking && !ParameterSenderService.AllParametersRelevantStatic;
        foreach (var parameter in UnifiedTracking.AllParameters)
        {
            var offered = !gate || SentAlongsideNativeFaceTracking.Contains(parameter)
                ? definitions
                : Array.Empty<IParameterDefinition>();
            relevant.AddRange(parameter.ResetParam(offered));
        }

        if (gate)
        {
            leftToVrchat.AddRange(definitions.Where(IsLeftToVrchat).Select(definition => definition.Name).Distinct());
        }

        return relevant;
    }

    private static bool IsLeftToVrchat(IParameterDefinition definition) =>
        LeftToVrchatNames.Value.Any(name =>
            ParamAddressMatcher.Matches(definition.Address, name)
            || ParamAddressMatcher.MatchesIndexed(definition.Address, name));
}
