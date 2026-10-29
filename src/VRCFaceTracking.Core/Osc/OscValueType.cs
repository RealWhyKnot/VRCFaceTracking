namespace VRCFaceTracking.Core.Osc;

internal enum OscValueType : byte
{
    Null = 0,
    Int = 1,
    Float = 2,
    Bool = 3,
    String = 4,
    ArrayBegin = 5,
    ArrayEnd = 6,
    Blob = 7,
}
