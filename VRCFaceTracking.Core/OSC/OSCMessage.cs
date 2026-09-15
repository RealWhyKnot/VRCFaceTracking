using System.Reflection;
using VRCFaceTracking.OSC;

namespace VRCFaceTracking.Core.OSC;

public class OscMessage : IDisposable
{
    internal readonly OscValue[] Values;
    private readonly Action<object> _valueSetter;
    private bool _disposed;

    public string Address
    {
        get; set;
    }

    public object Value
    {
        get => Values.Length == 0 ? null : Values[0].Value;
        set => _valueSetter(value);
    }

    public OscMessage(string address, Type type)
    {
        Address = address;
        var oscType = OscUtils.TypeConversions.FirstOrDefault(conv => conv.Key.Item1 == type).Value;

        if (oscType != default)
        {
            Values = new[] { new OscValue { Type = oscType.oscType } };
            _valueSetter = value => Values[0].Value = value;
        }
        else
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Values = new OscValue[fields.Length];
            for (var i = 0; i < Values.Length; i++)
            {
                Values[i] = new OscValue
                {
                    Type = OscUtils.TypeConversions.First(conv => conv.Key.Item1 == fields[i].FieldType).Value.oscType,
                };
            }
            _valueSetter = value =>
            {
                for (var j = 0; j < Values.Length; j++)
                {
                    Values[j].Value = fields[j].GetValue(value);
                }
            };
        }
    }

    internal OscMessage(string address, OscValue[] values)
    {
        Address = address;
        Values = values;
        _valueSetter = value => Values[0].Value = value;
    }

    public static OscMessage TryParseOsc(byte[] bytes, int len, ref int messageIndex) =>
        OscCodec.TryParse(bytes, len, ref messageIndex);

    public int Encode(byte[] buffer) => _disposed ? 0 : OscCodec.EncodeMessage(buffer, this);

    public void Dispose() => _disposed = true;
}
