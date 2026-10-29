using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public sealed class CapabilityAssignerTests
{
    private static CapabilityCandidate Dual(TrackingCapability? allowed = null) => new(EyeInitialized: true, ExpressionInitialized: true, allowed);
    private static CapabilityCandidate EyeOnly(TrackingCapability? allowed = null) => new(EyeInitialized: true, ExpressionInitialized: false, allowed);
    private static CapabilityCandidate ExpressionOnly(TrackingCapability? allowed = null) => new(EyeInitialized: false, ExpressionInitialized: true, allowed);

    [Fact]
    public void SingleDualModuleOwnsEverything()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([Dual()]);

        Assert.Equal(TrackingCapability.All, assigned[0]);
    }

    [Fact]
    public void EyeOnlyPlusExpressionOnlySplitByHalf()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([EyeOnly(), ExpressionOnly()]);

        Assert.Equal(TrackingCapability.Eyes | TrackingCapability.Brows | TrackingCapability.Head, assigned[0]);
        Assert.Equal(TrackingCapability.Mouth | TrackingCapability.Tongue | TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void TwoDualModulesFirstOwnsGroupsBothOwnHead()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([Dual(), Dual()]);

        Assert.Equal(TrackingCapability.All, assigned[0]);
        Assert.Equal(TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void FirstModuleRemovedSecondInherits()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([Dual()]);

        Assert.Equal(TrackingCapability.All, assigned[0]);
    }

    [Fact]
    public void OverrideRestrictingFirstHandsRestToSecond()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign(
        [
            Dual(TrackingCapability.Mouth | TrackingCapability.Tongue),
            Dual(),
        ]);

        Assert.Equal(TrackingCapability.Mouth | TrackingCapability.Tongue, assigned[0]);
        Assert.Equal(TrackingCapability.Eyes | TrackingCapability.Brows | TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void ExpressionOnlyAloneGainsEyeHalfGroupsViaGapFill()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([ExpressionOnly()]);

        Assert.Equal(TrackingCapability.All, assigned[0]);
    }

    [Fact]
    public void GapFillPrefersModuleWithMatchingHalf()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([ExpressionOnly(), EyeOnly()]);

        Assert.Equal(TrackingCapability.Mouth | TrackingCapability.Tongue | TrackingCapability.Head, assigned[0]);
        Assert.Equal(TrackingCapability.Eyes | TrackingCapability.Brows | TrackingCapability.Head, assigned[1]);
    }

    [Fact]
    public void NoneOverrideOwnsNothing()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([Dual(TrackingCapability.None), Dual()]);

        Assert.Equal(TrackingCapability.None, assigned[0]);
        Assert.Equal(TrackingCapability.All, assigned[1]);
    }

    [Fact]
    public void HeadExcludedByOverrideIsNotAssigned()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([Dual(TrackingCapability.All & ~TrackingCapability.Head)]);

        Assert.Equal(TrackingCapability.All & ~TrackingCapability.Head, assigned[0]);
    }

    [Fact]
    public void UninitializedModuleOwnsNothing()
    {
        TrackingCapability[] assigned = CapabilityAssigner.Assign([new CapabilityCandidate(EyeInitialized: false, ExpressionInitialized: false, AllowedOverride: null), Dual()]);

        Assert.Equal(TrackingCapability.None, assigned[0]);
        Assert.Equal(TrackingCapability.All, assigned[1]);
    }

    [Fact]
    public void EmptyInputYieldsEmptyResult() => Assert.Empty(CapabilityAssigner.Assign([]));
}
