namespace VRCFaceTracking.Core.Library;

public static class CapabilityAssigner
{
    private static readonly TrackingCapability[] ExclusiveGroups =
    [
        TrackingCapability.Eyes,
        TrackingCapability.Brows,
        TrackingCapability.Mouth,
        TrackingCapability.Tongue,
    ];

    public static TrackingCapability[] Assign(IReadOnlyList<CapabilityCandidate> modules)
    {
        var assigned = new TrackingCapability[modules.Count];
        foreach (TrackingCapability group in ExclusiveGroups)
        {
            int defaultHalfOwner = -1;
            int anyHalfOwner = -1;
            for (int i = 0; i < modules.Count; i++)
            {
                CapabilityCandidate candidate = modules[i];
                if (!candidate.Allowed.Allows(group))
                {
                    continue;
                }

                bool inDefaultHalf = TrackingCapabilities.EyeHalf.Allows(group)
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

            int owner = defaultHalfOwner >= 0 ? defaultHalfOwner : anyHalfOwner;
            if (owner >= 0)
            {
                assigned[owner] |= group;
            }
        }

        for (int i = 0; i < modules.Count; i++)
        {
            if (modules[i].AnyInitialized && modules[i].Allowed.Allows(TrackingCapability.Head))
            {
                assigned[i] |= TrackingCapability.Head;
            }
        }

        return assigned;
    }
}
