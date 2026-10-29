using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Perf.Tests;

internal static class Tracking
{
    public static void MatchHostProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            Core.PowerThrottling.TryOptOut(out _, out _);
        }
    }

    public static UnifiedTrackingMutator CreateMutator(bool allMutations)
    {
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
        if (allMutations)
        {
            foreach (TrackingMutation mutation in TrackingMutation.GetImplementingMutations())
            {
                mutation.Logger = NullLogger.Instance;
                mutation.IsActive = true;
                mutator.Mutations.Add(mutation);
            }
            mutator.Enabled = true;
            mutator.Initialize();
        }
        return mutator;
    }

    public static void SetAllParametersRelevant(bool relevant)
    {
        ParameterSenderService.AllParametersRelevantStatic = relevant;
        foreach (Parameter parameter in TrackingOutput.AllParameters)
        {
            parameter.ResetParam([]);
        }
        ParameterSenderService.Clear();
    }

    public static int Subscribers()
    {
        FieldInfo field = typeof(TrackingOutput).GetField(nameof(TrackingOutput.DataUpdated), BindingFlags.NonPublic | BindingFlags.Static)!;
        return (field.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    public static void WriteFrame(int frame)
    {
        float weight = frame % 100 / 100f;
        using (UnifiedTracking.LockData())
        {
            UnifiedTrackingData data = UnifiedTracking.Data;
            for (int i = 0; i < data.Shapes.Length; i++)
            {
                data.Shapes[i].Weight = weight;
            }
            data.Eye.Left.Openness = 1 - weight;
            data.Eye.Right.Openness = 1 - weight;
            data.Eye.Left.Gaze.x = weight - 0.5f;
            data.Eye.Right.Gaze.x = weight - 0.5f;
            data.Eye.Left.PupilDiameter_MM = 3 + weight;
            data.Eye.Right.PupilDiameter_MM = 3 + weight;
            data.Head.HeadYaw = weight - 0.5f;
        }
    }

    public static List<IParameterDefinition> AvatarDefinitions()
    {
        var definitions = new List<IParameterDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Parameter parameter in TrackingOutput.AllParameters)
        {
            foreach ((string? name, Parameter _) in parameter.GetParamNames())
            {
                if (!name.StartsWith("v2/", StringComparison.Ordinal) || !seen.Add(name))
                {
                    continue;
                }
                definitions.Add(new ParameterDefinition($"/avatar/parameters/FT/{name}", $"FT/{name}", typeof(float)));
                definitions.Add(new ParameterDefinition($"/avatar/parameters/FT/{name}Negative", $"FT/{name}Negative", typeof(bool)));
                foreach (int step in new[] { 1, 2, 4 })
                {
                    definitions.Add(new ParameterDefinition($"/avatar/parameters/FT/{name}{step}", $"FT/{name}{step}", typeof(bool)));
                }
            }
        }
        return definitions;
    }
}
