using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace VRCFaceTracking.Core.Osc;

public sealed class OscMessage
{
    internal readonly OscValue[] _values;
    private readonly Action<object?> _valueSetter;
    private readonly int[]? _floatFieldSlots;
    private byte[]? _addressBytes;

    public string Address
    {
        get;
        set
        {
            field = value;
            _addressBytes = null;
        }
    }

    internal ReadOnlySpan<byte> AddressBytes => _addressBytes ??= Encoding.UTF8.GetBytes(Address);

    public object? Value
    {
        get => _values.Length == 0 ? null : _values[0].Value;
        set => _valueSetter(value);
    }

    public OscMessage(string address, Type type)
    {
        Address = address;
        (OscValueType oscType, string configType) oscType = OscUtils.TypeConversions.FirstOrDefault(conv => conv.Key.Item1 == type).Value;

        if (oscType != default)
        {
            _values = [new OscValue { Type = oscType.oscType }];
            _valueSetter = value => _values[0].Value = value;
        }
        else
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            _values = new OscValue[fields.Length];
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] = new OscValue
                {
                    Type = OscUtils.TypeConversions.First(conv => conv.Key.Item1 == fields[i].FieldType).Value.oscType,
                };
            }
            _valueSetter = value =>
            {
                for (int j = 0; j < _values.Length; j++)
                {
                    _values[j].Value = fields[j].GetValue(value);
                }
            };

            if (type.IsLayoutSequential
                && fields.All(field => field.FieldType == typeof(float))
                && Marshal.SizeOf(type) == fields.Length * sizeof(float))
            {
                _floatFieldSlots = [.. fields.Select(field => (int)Marshal.OffsetOf(type, field.Name) / sizeof(float))];
            }
        }
    }

    internal OscMessage(string address, OscValue[] values)
    {
        Address = address;
        _values = values;
        _valueSetter = value => _values[0].Value = value;
    }

    public static OscMessage CreateBlob(string address, byte[] data) =>
        new(address, [new OscValue { Type = OscValueType.Blob, BlobValue = data }]);

    internal void SetValue<T>(T value) where T : struct
    {
        if (_values.Length == 1)
        {
            ref OscValue slot = ref _values[0];
            if (typeof(T) == typeof(float) && slot.Type == OscValueType.Float)
            {
                slot.FloatValue = Unsafe.As<T, float>(ref value);
                return;
            }

            if (typeof(T) == typeof(int) && slot.Type == OscValueType.Int)
            {
                slot.IntValue = Unsafe.As<T, int>(ref value);
                return;
            }

            if (typeof(T) == typeof(bool) && slot.Type == OscValueType.Bool)
            {
                slot.BoolValue = Unsafe.As<T, bool>(ref value);
                return;
            }
        }

        if (_floatFieldSlots != null && Unsafe.SizeOf<T>() == _floatFieldSlots.Length * sizeof(float))
        {
            ref float first = ref Unsafe.As<T, float>(ref value);
            for (int j = 0; j < _floatFieldSlots.Length; j++)
            {
                _values[j].FloatValue = Unsafe.Add(ref first, _floatFieldSlots[j]);
            }
            return;
        }

        Value = value;
    }
}
