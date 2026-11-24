namespace VRCFaceTracking.Core.Params.Data.Mutation;

[AttributeUsage(AttributeTargets.Method)]
public sealed class MutationButtonAttribute : Attribute
{
    public MutationButtonAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool Developer { get; set; }

    public TrackingCard Card { get; set; }

    public string? Group { get; set; }

    public int Order { get; set; }

    public bool Advanced { get; set; }

    public bool Confirm { get; set; }
}
