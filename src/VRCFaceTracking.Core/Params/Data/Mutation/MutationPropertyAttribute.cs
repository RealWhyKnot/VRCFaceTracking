namespace VRCFaceTracking.Core.Params.Data.Mutation;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MutationPropertyAttribute : Attribute
{
    public MutationPropertyAttribute(string name, bool savedImmediate = false, float min = 0f, float max = 1f)
    {
        Name = name;
        SavedImmediate = savedImmediate;
        Min = min;
        Max = max;
    }

    public string Name { get; }

    public bool SavedImmediate { get; }

    public float Min { get; }

    public float Max { get; }

    public bool Developer { get; set; }

    public string? Group { get; set; }

    public bool Indented { get; set; }
}
