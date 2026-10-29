using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Osc.DataTypes;

public class BaseParam<T> : Parameter where T : struct
{
    private const string DefaultPrefix = "/avatar/parameters/";
    protected const string CurrentVersionPrefix = "v2/";

    private readonly Func<UnifiedTrackingData, T> _getValueFunc;

    private readonly string _paramName;
    private readonly bool _sendOnLoad;

    public bool Relevant
    {
        get;
        protected set
        {
            if (_sendOnLoad && value)
            {
                Enqueue();
            }

            if (field == value)
            {
                return;
            }

            field = value;

            if (_getValueFunc == null)
            {
                return;
            }

            if (value)
            {
                TrackingOutput.DataUpdated += Process;
            }
            else
            {
                TrackingOutput.DataUpdated -= Process;
            }
        }
    }

    private T? _lastValue;

    public T ParamValue
    {
        get => (T)OscMessage.Value!;
        set
        {
            if (_lastValue.HasValue && EqualityComparer<T>.Default.Equals(value, _lastValue.GetValueOrDefault()))
            {
                return;
            }

            OscMessage.SetValue(value);
            _lastValue = value;
            Enqueue();
        }
    }

    private void Enqueue() => ParameterSenderService.Enqueue(OscMessage);

    protected OscMessage OscMessage { get; }

    public BaseParam(string name, Func<UnifiedTrackingData, T> getValueFunc, bool sendOnLoad = false)
    {
        _paramName = name;
        _getValueFunc = getValueFunc;
        OscMessage = new OscMessage(DefaultPrefix + name, typeof(T));
        _sendOnLoad = sendOnLoad;
    }

    public override Parameter[] ResetParam(IReadOnlyList<IParameterDefinition> newParams)
    {
        if (ParameterSenderService.AllParametersRelevantStatic)
        {
            Relevant = true;
            OscMessage.Address = DefaultPrefix + _paramName;

            return [this];
        }

        IParameterDefinition? compatibleParam = newParams.FirstOrDefault(param =>
            param.Type == typeof(T)
            && ParamAddressMatcher.Matches(param.Address, _paramName));

        if (compatibleParam != null)
        {
            Relevant = true;
            OscMessage.Address = compatibleParam.Address;
        }
        else
        {
            Relevant = false;
            OscMessage.Address = DefaultPrefix + _paramName;
        }

        return Relevant ? [this] : [];
    }

    public override (string, Parameter)[] GetParamNames() => [(_paramName, this)];

    public override bool Deprecated => !_paramName.StartsWith(CurrentVersionPrefix, StringComparison.Ordinal);

    protected virtual void Process(object? sender, TrackingDataEventArgs e) => ParamValue = _getValueFunc.Invoke(e.Data);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TrackingOutput.DataUpdated -= Process;
        }

        base.Dispose(disposing);
    }
}
