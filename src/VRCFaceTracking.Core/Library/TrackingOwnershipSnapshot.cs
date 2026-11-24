namespace VRCFaceTracking.Core.Library;

public sealed record TrackingOwnershipSnapshot(CapabilityOwnership Eyes, CapabilityOwnership Mouth)
{
    public static TrackingOwnershipSnapshot Empty { get; } = new(Eyes: new CapabilityOwnership(Owner: null, Claimants: []), Mouth: new CapabilityOwnership(Owner: null, Claimants: []));

    public bool HasOwnershipConflict => Eyes.HasConflict || Mouth.HasConflict;

    public IReadOnlyList<string> ConflictingModules => [.. (Eyes.HasConflict ? Eyes.Claimants : [])
        .Concat(Mouth.HasConflict ? Mouth.Claimants : [])
        .Distinct(StringComparer.Ordinal)];

    public static TrackingOwnershipSnapshot Build(IReadOnlyList<ModuleClaim> modules) => new(
        Describe(modules, TrackingCapability.Eyes, claim => claim.EyeInitialized),
        Describe(modules, TrackingCapability.Mouth, claim => claim.ExpressionInitialized));

    private static CapabilityOwnership Describe(IReadOnlyList<ModuleClaim> modules, TrackingCapability capability, Func<ModuleClaim, bool> initialized)
    {
        string? owner = null;
        List<string> claimants = [];
        foreach (ModuleClaim module in modules)
        {
            if (initialized(module) && module.Allowed.Allows(capability))
            {
                claimants.Add(module.Name);
            }

            if (owner == null && module.Effective.Allows(capability))
            {
                owner = module.Name;
            }
        }

        return new CapabilityOwnership(owner, claimants);
    }
}
