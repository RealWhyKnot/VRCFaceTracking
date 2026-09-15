namespace VRCFaceTracking.Core.OSC;

public enum OscValueType : byte
{
    Null = 0,
    Int = 1,
    Float = 2,
    Bool = 3,
    String = 4,
    ArrayBegin = 5,
    ArrayEnd = 6,
}

public struct OscValue
{
    public OscValueType Type;
    public int IntValue;
    public float FloatValue;
    public bool BoolValue;
    public string StringValue;

    public object Value
    {
        get => Type switch
        {
            OscValueType.Int => IntValue,
            OscValueType.Float => FloatValue,
            OscValueType.Bool => BoolValue,
            OscValueType.String => StringValue,
            _ => null
        };
        set
        {
            switch (Type)
            {
                case OscValueType.Int:
                    IntValue = (int)value;
                    break;
                case OscValueType.Float:
                    FloatValue = (float)value;
                    break;
                case OscValueType.Bool:
                    BoolValue = (bool)value;
                    break;
                case OscValueType.String:
                    StringValue = (string)value;
                    break;
            }
        }
    }
}
