using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Params.Expressions.Legacy.Eye;
using VRCFaceTracking.Core.Params.Expressions.Legacy.Lip;

namespace VRCFaceTracking.Core.Params;

public static class TrackingOutput
{
    public static readonly Parameter[] LegacyParameters = [.. LipShapeMerger.AllLipParameters.Union(EyeTrackingParams.ParameterList)];

    public static readonly Parameter[] ExpressionParameters = UnifiedExpressionsParameters.ExpressionParameters;

    public static readonly Parameter[] HeadParameters = UnifiedHeadParameters.HeadParameters;

    public static readonly Parameter[] AllParameters = [.. ExpressionParameters, .. LegacyParameters, .. HeadParameters];

    private static TrackingDataEventArgs? LastArgs;

    public static UnifiedTrackingMutator Mutator { get; set; } = null!;

    public static event EventHandler<TrackingDataEventArgs>? DataUpdated;

    public static void UpdateData()
    {
        UnifiedTrackingData data = Mutator.MutateData(UnifiedTracking.Data);
        TrackingDataEventArgs? args = LastArgs;
        if (args is null || !ReferenceEquals(args.Data, data))
        {
            args = new TrackingDataEventArgs(data);
            LastArgs = args;
        }

        DataUpdated?.Invoke(sender: null, args);
    }
}
