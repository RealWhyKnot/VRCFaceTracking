using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Core.Params.Data;

public sealed record AvatarGapReport(IReadOnlyList<AvatarGapEntry> Entries)
{
    public int TrackedCount => Entries.Count;

    public int CoveredCount => Count(AvatarGapStatus.Covered);

    public int FilledCount => Count(AvatarGapStatus.Filled);

    public int MissingCount => Count(AvatarGapStatus.Missing);

    public int NativeSkipCount => Count(AvatarGapStatus.NativeSkip);

    public int AbsentCount => FilledCount + MissingCount;

    public bool IsEmpty => Entries.Count == 0;

    public static AvatarGapReport Build(SignalCapabilitySnapshot capabilities, AvatarSupport support, IReadOnlyList<SignalRoute> activeRoutes)
    {
        List<AvatarGapEntry> entries = [];
        foreach (SignalGroup group in SignalShapes.Groups)
        {
            if (!capabilities.IsLive(group.Primary, group.Partner))
            {
                continue;
            }

            if (support.UsesNativeFaceTracking)
            {
                entries.Add(new AvatarGapEntry(group.Primary, AvatarGapStatus.NativeSkip, FillSource: null));
                continue;
            }

            if (support.Drives(group.Primary, group.Partner))
            {
                entries.Add(new AvatarGapEntry(group.Primary, AvatarGapStatus.Covered, FillSource: null));
                continue;
            }

            string? source = FillSource(group, capabilities, support, activeRoutes);
            entries.Add(new AvatarGapEntry(group.Primary, source is null ? AvatarGapStatus.Missing : AvatarGapStatus.Filled, source));
        }

        return new AvatarGapReport(entries);
    }

    public bool SameAs(AvatarGapReport? other) => other is not null && Entries.SequenceEqual(other.Entries);

    public string Describe(AvatarGapStatus status)
    {
        List<string> names = [];
        foreach (AvatarGapEntry entry in Entries)
        {
            if (entry.Status == status)
            {
                names.Add(entry.Status == AvatarGapStatus.Filled ? $"{entry.Name} via {entry.FillSource}" : entry.Name);
            }
        }

        return string.Join(", ", names);
    }

    private static string? FillSource(SignalGroup group, SignalCapabilitySnapshot capabilities, AvatarSupport support, IReadOnlyList<SignalRoute> activeRoutes)
    {
        foreach (SignalRoute route in activeRoutes)
        {
            if (route.Kind != SignalRouteKind.Copy || !IsSource(route, group))
            {
                continue;
            }

            if (!support.Drives(route.TargetLeft, route.TargetRight))
            {
                continue;
            }

            if (route.Strict && capabilities.IsLive(route.TargetLeft, route.TargetRight))
            {
                continue;
            }

            return SignalShapes.SignalName(route.TargetLeft);
        }

        return null;
    }

    private static bool IsSource(SignalRoute route, SignalGroup group) =>
        (route.SourceLeft is { } left && (left == group.Primary || left == group.Partner))
        || (route.SourceRight is { } right && (right == group.Primary || right == group.Partner));

    private int Count(AvatarGapStatus status)
    {
        int count = 0;
        foreach (AvatarGapEntry entry in Entries)
        {
            if (entry.Status == status)
            {
                count++;
            }
        }

        return count;
    }
}
