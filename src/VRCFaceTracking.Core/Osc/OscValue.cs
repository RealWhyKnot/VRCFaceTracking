namespace VRCFaceTracking.Core.Osc;

internal struct OscValue
{
    public OscValueType Type { get; set; }
    public int IntValue { get; set; }
    public float FloatValue { get; set; }
    public bool BoolValue { get; set; }
    public string? StringValue { get; set; }
    public byte[]? BlobValue { get; set; }

    public object? Value
    {
        readonly get => Type switch
        {
            OscValueType.Int => IntValue,
            OscValueType.Float => FloatValue,
            OscValueType.Bool => BoolValue,
            OscValueType.String => StringValue,
            OscValueType.Blob => BlobValue,
            OscValueType.Null or OscValueType.ArrayBegin or OscValueType.ArrayEnd => null,
            _ => null,
        };
        set
        {
            switch (Type)
            {
                case OscValueType.Int:
                    IntValue = (int)value!;
                    break;
                case OscValueType.Float:
                    FloatValue = (float)value!;
                    break;
                case OscValueType.Bool:
                    BoolValue = (bool)value!;
                    break;
                case OscValueType.String:
                    StringValue = (string?)value;
                    break;
                case OscValueType.Blob:
                    BlobValue = (byte[]?)value;
                    break;
                case OscValueType.Null:
                case OscValueType.ArrayBegin:
                case OscValueType.ArrayEnd:
                default:
                    break;
            }
        }
    }
}
