using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Params.DataTypes;

public sealed class NativeParameter<T> : AlwaysRelevantParameter<T> where T : struct
{
    private readonly Func<IReadOnlyList<IParameterDefinition>, bool> _condition;

    public NativeParameter(Func<UnifiedTrackingData, T> getValueFunc, Func<IReadOnlyList<IParameterDefinition>, bool> condition, string paramAddress) : base(getValueFunc, paramAddress)
    {
        _condition = condition;
    }

    public override Parameter[] ResetParam(IReadOnlyList<IParameterDefinition> newParams)
    {
        if (_condition.Invoke(newParams))
        {
            return base.ResetParam(newParams);
        }

        Relevant = false;
        return [];

    }
}
