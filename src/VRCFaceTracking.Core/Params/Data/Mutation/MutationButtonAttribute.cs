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
}
