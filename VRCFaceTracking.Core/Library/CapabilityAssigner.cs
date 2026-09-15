namespace VRCFaceTracking.Core.Library;

public readonly record struct CapabilityCandidate(bool EyeInitialized, bool ExpressionInitialized, TrackingCapability? AllowedOverride)
{
    public TrackingCapability Allowed => AllowedOverride ?? TrackingCapability.All;
    public bool AnyInitialized => EyeInitialized || ExpressionInitialized;
}

public static class CapabilityAssigner
{
    private static readonly TrackingCapability[] ExclusiveGroups =
    {
        TrackingCapability.Eyes,
        TrackingCapability.Brows,
        TrackingCapability.Mouth,
        TrackingCapability.Tongue,
    };

    public static TrackingCapability[] Assign(IReadOnlyList<CapabilityCandidate> modules)
    {
        var assigned = new TrackingCapability[modules.Count];
        foreach (var group in ExclusiveGroups)
        {
            var defaultHalfOwner = -1;
            var anyHalfOwner = -1;
            for (var i = 0; i < modules.Count; i++)
            {
                var candidate = modules[i];
                if (!candidate.Allowed.HasFlag(group))
                {
                    continue;
                }

                var inDefaultHalf = TrackingCapabilities.EyeHalf.HasFlag(group)
                    ? candidate.EyeInitialized
                    : candidate.ExpressionInitialized;
                if (inDefaultHalf && defaultHalfOwner < 0)
                {
                    defaultHalfOwner = i;
                }
                if (candidate.AnyInitialized && anyHalfOwner < 0)
                {
                    anyHalfOwner = i;
                }
            }

            var owner = defaultHalfOwner >= 0 ? defaultHalfOwner : anyHalfOwner;
            if (owner >= 0)
            {
                assigned[owner] |= group;
            }
        }

        for (var i = 0; i < modules.Count; i++)
        {
            if (modules[i].AnyInitialized && modules[i].Allowed.HasFlag(TrackingCapability.Head))
            {
                assigned[i] |= TrackingCapability.Head;
            }
        }

        return assigned;
    }
}
