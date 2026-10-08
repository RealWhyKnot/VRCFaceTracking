using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VRCFaceTracking.OSC;

namespace VRCFaceTracking.Core.OSC;

public class OscMessage : IDisposable
{
    internal readonly OscValue[] Values;
    private readonly Action<object> _valueSetter;
    private readonly int[]? _floatFieldSlots;
    private bool _disposed;

    private string _address;
    private byte[] _addressBytes;

    public string Address
    {
        get => _address;
        set
        {
            _address = value;
            _addressBytes = null;
        }
    }

    internal ReadOnlySpan<byte> AddressBytes => _addressBytes ??= Encoding.UTF8.GetBytes(_address ?? string.Empty);

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

            if (type.IsLayoutSequential
                && fields.All(field => field.FieldType == typeof(float))
                && Marshal.SizeOf(type) == fields.Length * sizeof(float))
            {
                _floatFieldSlots = fields.Select(field => (int)Marshal.OffsetOf(type, field.Name) / sizeof(float)).ToArray();
            }
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

    internal void SetValue<T>(T value) where T : struct
    {
        if (Values.Length == 1)
        {
            ref var slot = ref Values[0];
            switch (slot.Type)
            {
                case OscValueType.Float when typeof(T) == typeof(float):
                    slot.FloatValue = Unsafe.As<T, float>(ref value);
                    return;
                case OscValueType.Int when typeof(T) == typeof(int):
                    slot.IntValue = Unsafe.As<T, int>(ref value);
                    return;
                case OscValueType.Bool when typeof(T) == typeof(bool):
                    slot.BoolValue = Unsafe.As<T, bool>(ref value);
                    return;
            }
        }

        if (_floatFieldSlots != null && Unsafe.SizeOf<T>() == _floatFieldSlots.Length * sizeof(float))
        {
            ref var first = ref Unsafe.As<T, float>(ref value);
            for (var j = 0; j < _floatFieldSlots.Length; j++)
            {
                Values[j].FloatValue = Unsafe.Add(ref first, _floatFieldSlots[j]);
            }
            return;
        }

        Value = value;
    }

    public int Encode(byte[] buffer) => _disposed ? 0 : OscCodec.EncodeMessage(buffer, this);

    public void Dispose() => _disposed = true;
}
