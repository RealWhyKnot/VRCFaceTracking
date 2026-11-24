namespace VRCFaceTracking.Core.Library;

public sealed record CapabilityOwnership(string? Owner, IReadOnlyList<string> Claimants)
{
    public bool HasConflict => Claimants.Count > 1;
}
