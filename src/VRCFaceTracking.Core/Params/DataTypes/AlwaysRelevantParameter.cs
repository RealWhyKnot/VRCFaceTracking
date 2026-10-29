using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Osc.DataTypes;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Params.DataTypes;

public class AlwaysRelevantParameter<T> : BaseParam<T> where T : struct
{
    public AlwaysRelevantParameter(Func<UnifiedTrackingData, T> getValueFunc,
        string paramAddress)
        : base(CurrentVersionPrefix, getValueFunc)
    {
        OscMessage.Address = paramAddress;
        Relevant = true;
    }

    public override Parameter[] ResetParam(IReadOnlyList<IParameterDefinition> newParams)
    {
        Relevant = true;
        return [this];
    }
}
