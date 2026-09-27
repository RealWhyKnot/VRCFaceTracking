using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public class ModuleSampleAcceptTests
{
    [Fact]
    public void NewerSample_IsAccepted()
    {
        var module = new ModuleRuntimeInfo();

        Assert.True(module.TryAcceptSample(100, TrackingCapability.All));
        Assert.True(module.TryAcceptSample(101, TrackingCapability.All));
    }

    [Fact]
    public void RepeatedSample_IsRejected()
    {
        var module = new ModuleRuntimeInfo();
        module.TryAcceptSample(100, TrackingCapability.All);

        Assert.False(module.TryAcceptSample(100, TrackingCapability.All));
    }

    [Fact]
    public void OlderSample_IsRejected()
    {
        var module = new ModuleRuntimeInfo();
        module.TryAcceptSample(100, TrackingCapability.All);

        Assert.False(module.TryAcceptSample(99, TrackingCapability.All));
    }

    [Fact]
    public void RepeatedSample_WithChangedCapabilities_IsAcceptedOnce()
    {
        var module = new ModuleRuntimeInfo();
        module.TryAcceptSample(100, TrackingCapability.Eyes);

        Assert.True(module.TryAcceptSample(100, TrackingCapability.Eyes | TrackingCapability.Mouth));
        Assert.False(module.TryAcceptSample(100, TrackingCapability.Eyes | TrackingCapability.Mouth));
    }

    [Fact]
    public void UnstampedSample_IsAlwaysAccepted()
    {
        var module = new ModuleRuntimeInfo();
        module.TryAcceptSample(100, TrackingCapability.All);

        Assert.True(module.TryAcceptSample(0, TrackingCapability.All));
        Assert.True(module.TryAcceptSample(0, TrackingCapability.All));
    }

    [Fact]
    public void Modules_TrackSamplesIndependently()
    {
        var first = new ModuleRuntimeInfo();
        var second = new ModuleRuntimeInfo();
        first.TryAcceptSample(200, TrackingCapability.Eyes);

        Assert.True(second.TryAcceptSample(150, TrackingCapability.Mouth));
    }

    [Fact]
    public void RestartedModule_AcceptsItsFirstSample()
    {
        var before = new ModuleRuntimeInfo();
        before.TryAcceptSample(500, TrackingCapability.All);
        var after = new ModuleRuntimeInfo();

        Assert.True(after.TryAcceptSample(501, TrackingCapability.All));
    }
}
