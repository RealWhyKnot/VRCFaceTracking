using VRCFaceTracking.Core.Osc.DataTypes;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Params.DataTypes;

public sealed class ConditionalBoolParameter : BaseParam<bool>
{
    private readonly Func<UnifiedTrackingData, (bool, bool)> _conditionalValueFunc;

    public ConditionalBoolParameter(Func<UnifiedTrackingData, (bool, bool)> getValueFunc, string paramName) :
        base(paramName, exp => getValueFunc.Invoke(exp).Item1)
    {
        _conditionalValueFunc = getValueFunc;
    }

    public override bool Deprecated => false;

    protected override void Process(object? sender, TrackingDataEventArgs e)
    {
        if (_conditionalValueFunc.Invoke(e.Data).Item2)
        {
            base.Process(sender, e);
        }
    }
}
