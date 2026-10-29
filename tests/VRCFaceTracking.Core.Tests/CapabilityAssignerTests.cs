using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public class CapabilityAssignerTests
{
    private static CapabilityCandidate Dual(TrackingCapability? allowed = null) => new(true, true, allowed);
    private static CapabilityCandidate EyeOnly(TrackingCapability? allowed = null) => new(true, false, allowed);
    private static CapabilityCandidate ExpressionOnly(TrackingCapability? allowed = null) => new(false, true, allowed);

    [Fact]
    public void SingleDualModule_OwnsEverything()
    {
        var assigned = CapabilityAssigner.Assign(new[] { Dual() });

        Assert.Equal(TrackingCapability.All, assigned[0]);
    }

    [Fact]
    public void EyeOnlyPlusExpressionOnly_SplitByHalf()
    {
        var assigned = CapabilityAssigner.Assign(new[] { EyeOnly(), ExpressionOnly() });

        Assert.Equal(TrackingCapability.Eyes | TrackingCapability.Brows | TrackingCapability.Head, assigned[0]);
        Assert.Equal(TrackingCapability.Mouth | TrackingCapability.Tongue | TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void TwoDualModules_FirstOwnsGroups_BothOwnHead()
    {
        var assigned = CapabilityAssigner.Assign(new[] { Dual(), Dual() });

        Assert.Equal(TrackingCapability.All, assigned[0]);
        Assert.Equal(TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void FirstModuleRemoved_SecondInherits()
    {
        var assigned = CapabilityAssigner.Assign(new[] { Dual() });

        Assert.Equal(TrackingCapability.All, assigned[0]);
    }

    [Fact]
    public void OverrideRestrictingFirst_HandsRestToSecond()
    {
        var assigned = CapabilityAssigner.Assign(new[]
        {
            Dual(TrackingCapability.Mouth | TrackingCapability.Tongue),
            Dual(),
        });

        Assert.Equal(TrackingCapability.Mouth | TrackingCapability.Tongue, assigned[0]);
        Assert.Equal(TrackingCapability.Eyes | TrackingCapability.Brows | TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void ExpressionOnlyAlone_GainsEyeHalfGroupsViaGapFill()
    {
        var assigned = CapabilityAssigner.Assign(new[] { ExpressionOnly() });

        Assert.Equal(TrackingCapability.All, assigned[0]);
    }

    [Fact]
    public void GapFill_PrefersModuleWithMatchingHalf()
    {
        var assigned = CapabilityAssigner.Assign(new[] { ExpressionOnly(), EyeOnly() });

        Assert.Equal(TrackingCapability.Mouth | TrackingCapability.Tongue | TrackingCapability.Head, assigned[0]);
        Assert.Equal(TrackingCapability.Eyes | TrackingCapability.Brows | TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void NoneOverride_OwnsNothing()
    {
        var assigned = CapabilityAssigner.Assign(new[] { Dual(TrackingCapability.None), Dual() });

        Assert.Equal(TrackingCapability.None, assigned[0]);
        Assert.Equal(TrackingCapability.All, assigned[1]);
    }

    [Fact]
    public void HeadExcludedByOverride_IsNotAssigned()
    {
        var assigned = CapabilityAssigner.Assign(new[] { Dual(TrackingCapability.All & ~TrackingCapability.Head) });

        Assert.Equal(TrackingCapability.All & ~TrackingCapability.Head, assigned[0]);
    }

    [Fact]
    public void UninitializedModule_OwnsNothing()
    {
        var assigned = CapabilityAssigner.Assign(new[] { new CapabilityCandidate(false, false, null), Dual() });

        Assert.Equal(TrackingCapability.None, assigned[0]);
        Assert.Equal(TrackingCapability.All, assigned[1]);
    }

    [Fact]
    public void EmptyInput_YieldsEmptyResult()
    {
        Assert.Empty(CapabilityAssigner.Assign(Array.Empty<CapabilityCandidate>()));
    }
}
