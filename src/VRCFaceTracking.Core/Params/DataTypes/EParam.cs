using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Osc.DataTypes;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Params.DataTypes;

public sealed class EParam : Parameter
{
    private readonly List<Parameter> _parameter = [];

    public EParam(string paramName, Func<UnifiedTrackingData, float> getValueFunc, float minBoolThreshold = 0.5f,
        bool skipBinaryParamCreation = false)
    {
        _parameter.Add(new BaseParam<bool>(paramName, exp => getValueFunc.Invoke(exp) < minBoolThreshold));
        _parameter.Add(new BaseParam<float>(paramName, getValueFunc));
        if (!skipBinaryParamCreation)
        {
            _parameter.Add(new BinaryBaseParameter(paramName, getValueFunc));
        }
    }

    public EParam(string paramName, Func<UnifiedTrackingData, Vector2> getValueFunc, float minBoolThreshold = 0.5f)
    {
        _parameter.Add(new BaseParam<bool>(paramName + "X", exp => getValueFunc.Invoke(exp).x < minBoolThreshold));
        _parameter.Add(new BaseParam<float>(paramName + "X", exp => getValueFunc.Invoke(exp).x));
        _parameter.Add(new BinaryBaseParameter(paramName + "X", exp => getValueFunc.Invoke(exp).x));

        _parameter.Add(new BaseParam<bool>(paramName + "Y", exp => getValueFunc.Invoke(exp).y < minBoolThreshold));
        _parameter.Add(new BaseParam<float>(paramName + "Y", exp => getValueFunc.Invoke(exp).y));
        _parameter.Add(new BinaryBaseParameter(paramName + "Y", exp => getValueFunc.Invoke(exp).y));
    }

    public override Parameter[] ResetParam(IReadOnlyList<IParameterDefinition> newParams) => [.. _parameter.SelectMany(param => param.ResetParam(newParams))];

    public override (string, Parameter)[] GetParamNames() => [.. _parameter.SelectMany(param => param.GetParamNames())];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Parameter parameter in _parameter)
            {
                parameter.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
