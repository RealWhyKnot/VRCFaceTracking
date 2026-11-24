namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed record MutationMetadata(
    TrackingMutation Owner,
    string Key,
    TrackingCard Card,
    int Order,
    int Index,
    bool Advanced,
    bool Developer,
    string? DependsOn,
    float Step,
    bool Confirm,
    object? DefaultValue);
