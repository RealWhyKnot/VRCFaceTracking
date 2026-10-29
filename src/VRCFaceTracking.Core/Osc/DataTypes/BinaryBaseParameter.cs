using System.Globalization;
using System.Numerics;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;

namespace VRCFaceTracking.Core.Osc.DataTypes;

public sealed class BinaryBaseParameter : Parameter
{
    private readonly List<BaseParam<bool>> _params = [];

    private readonly BaseParam<bool> _negativeParam;
    private int _maxPossibleBinaryInt;
    private readonly string _paramName;
    private readonly Func<UnifiedTrackingData, float> _getValueFunc;

    private bool ProcessBinary(UnifiedTrackingData data, int binaryIndex)
    {
        float value = _getValueFunc.Invoke(data);
        if (!_negativeParam.Relevant &&
            value < 0)
        {
            return false;
        }

        value = Math.Abs(value);

        if (value > 0.99999f)
        {
            return true;
        }

        int bigValue = (int)(value * _maxPossibleBinaryInt);

        return ((bigValue >> binaryIndex) & 1) == 1;
    }

    private void DisposeBinaryParams()
    {
        foreach (BaseParam<bool> param in _params)
        {
            param.Dispose();
        }

        _params.Clear();
    }

    public override Parameter[] ResetParam(IReadOnlyList<IParameterDefinition> newParams)
    {
        DisposeBinaryParams();
        Parameter[] negativeRelevancy = _negativeParam.ResetParam(newParams);

        IEnumerable<IParameterDefinition> boolParams = newParams.Where(p =>
            p.Type == typeof(bool) && ParamAddressMatcher.MatchesIndexed(p.Address, _paramName));

        var paramsToCreate = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IParameterDefinition? param in boolParams)
        {
            string tempName = param.Name;
            if (!int.TryParse(
                    string.Concat(tempName.Replace(_paramName, "", StringComparison.Ordinal).Reverse().TakeWhile(char.IsNumber)
                        .Reverse()), CultureInfo.InvariantCulture, out int index))
            {
                continue;
            }

            int? binaryIndex = GetBinarySteps(index);
            if (binaryIndex.HasValue)
            {
                _ = paramsToCreate.TryAdd(_paramName + index, binaryIndex.Value);
            }
        }

        if (paramsToCreate.Count == 0)
        {
            return negativeRelevancy;
        }

        _maxPossibleBinaryInt = (int)Math.Pow(2, paramsToCreate.Values.Count);
        var parameters = new List<Parameter>(negativeRelevancy);
        foreach (BaseParam<bool>? newBool in paramsToCreate
                     .Select(param =>
                         new BaseParam<bool>(param.Key, data => ProcessBinary(data, param.Value), sendOnLoad: true)))
        {
            parameters.AddRange(newBool.ResetParam(newParams));
            _params.Add(newBool);
        }

        return [.. parameters];
    }

    public override (string, Parameter)[] GetParamNames() =>
        [.. _params.SelectMany(p => p.GetParamNames()), .. _negativeParam.GetParamNames()];

    internal static int? GetBinarySteps(int index) =>
        index > 0 && BitOperations.IsPow2(index) ? BitOperations.Log2((uint)index) : null;

    public BinaryBaseParameter(string paramName, Func<UnifiedTrackingData, float> getValueFunc)
    {
        _paramName = paramName;
        _getValueFunc = getValueFunc;
        _negativeParam =
            new BaseParam<bool>(paramName + "Negative", data => getValueFunc.Invoke(data) < 0, sendOnLoad: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeBinaryParams();
            _negativeParam.Dispose();
        }

        base.Dispose(disposing);
    }
}
