namespace VRCFaceTracking.Core.Params.Data.Mutation;

public interface IMutationComponent
{
    string Name { get; }

    string? Group { get; set; }

    bool StartsGroup { get; set; }
}
