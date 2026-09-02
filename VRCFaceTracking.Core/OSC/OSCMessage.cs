using System.Reflection;
using System.Runtime.InteropServices;
using VRCFaceTracking.OSC;

namespace VRCFaceTracking.Core.OSC;

public class OscMessage
{
    private static readonly int AddressOffset = (int)Marshal.OffsetOf<OscMessageMeta>(nameof(OscMessageMeta.Address));
    private static readonly int ValueLengthOffset = (int)Marshal.OffsetOf<OscMessageMeta>(nameof(OscMessageMeta.ValueLength));
    private static readonly int ValueOffset = (int)Marshal.OffsetOf<OscMessageMeta>(nameof(OscMessageMeta.Value));

    public OscMessageMeta _meta;
    private readonly IntPtr _metaPtr;

    public string Address
    {
        get => _meta.Address;
        set => _meta.Address = value;
    }

    private readonly Action<object> _valueSetter;

    public object Value
    {
        get
        {
            if (_meta.ValueLength == 0)
            {
                return null;
            }

            return Marshal.PtrToStructure<OscValue>(_meta.Value).Value;
        }
        set => _valueSetter(value);
    }

    public OscMessage(string address, Type type)
    {
        Address = address;
        var oscType = OscUtils.TypeConversions.FirstOrDefault(conv => conv.Key.Item1 == type).Value;

        if (oscType != default)
        {
            _meta.ValueLength = 1;
            _meta.Value = Marshal.AllocHGlobal(Marshal.SizeOf<OscValue>() * _meta.ValueLength);
            var oscValue = new OscValue
            {
                Type = oscType.oscType,
            };
            _valueSetter = value =>
            {
                oscValue.Value = value;
                Marshal.StructureToPtr(oscValue, _meta.Value, false);
            };
        }
        else    // If we don't have the type, we assume it's a struct and serialize it using reflection
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            _meta.ValueLength = fields.Length;
            _meta.Value = Marshal.AllocHGlobal(Marshal.SizeOf<OscValue>() * _meta.ValueLength);
            var values = new OscValue[_meta.ValueLength];
            for (var i = 0; i < _meta.ValueLength; i++)
            {
                values[i] = new OscValue
                {
                    Type = OscUtils.TypeConversions.First(conv => conv.Key.Item1 == fields[i].FieldType).Value.oscType,
                };
            }
            _valueSetter = value =>
            {
                for (var j = 0; j < _meta.ValueLength; j++)
                {
                    values[j].Value = fields[j].GetValue(value);
                    Marshal.StructureToPtr(values[j], _meta.Value + Marshal.SizeOf<OscValue>() * j, false);
                }
            };
        }
    }

    public static OscMessage TryParseOsc(byte[] bytes, int len, ref int messageIndex)
    {
        var msg = new OscMessage(bytes, len, ref messageIndex);
        if (msg._metaPtr == IntPtr.Zero)
        {
            return null;
        }

        return msg;
    }

    public OscMessage(byte[] bytes, int len, ref int messageIndex)
    {
        _metaPtr = fti_osc.parse_osc(bytes, len, ref messageIndex);
        if (_metaPtr != IntPtr.Zero)
        {
            _meta.Address = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(_metaPtr, AddressOffset));
            _meta.ValueLength = Marshal.ReadInt32(_metaPtr, ValueLengthOffset);
            _meta.Value = Marshal.ReadIntPtr(_metaPtr, ValueOffset);
        }
    }

    /// <summary>
    /// Encodes stored osc meta into raw bytes using fti_osc lib
    /// </summary>
    /// <param name="buffer">Target byte buffer to serialize to, starting from index 0</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>Length of serialized data</returns>
    public int Encode(byte[] buffer) => fti_osc.create_osc_message(buffer, ref _meta);

    public OscMessage(OscMessageMeta meta) => _meta = meta;

    ~OscMessage()
    {
        // If we don't own this memory, then we need to sent it back to rust to free it
        if (_metaPtr != IntPtr.Zero)
        {
            fti_osc.free_osc_message(_metaPtr);
        }
    }
}