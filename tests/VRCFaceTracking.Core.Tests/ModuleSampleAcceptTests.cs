using System.Diagnostics;
using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public sealed class ModuleSampleAcceptTests
{
    private static ModuleRuntimeInfo NewModule() => new()
    {
        SandboxModulePath = string.Empty,
        Process = new Process(),
        ModuleInformation = new ModuleMetadataInternal(),
        ModuleClassName = string.Empty,
    };

    [Fact]
    public void NewerSampleIsAccepted()
    {
        ModuleRuntimeInfo module = NewModule();

        Assert.True(module.TryAcceptSample(100, TrackingCapability.All));
        Assert.True(module.TryAcceptSample(101, TrackingCapability.All));
    }

    [Fact]
    public void RepeatedSampleIsRejected()
    {
        ModuleRuntimeInfo module = NewModule();
        module.TryAcceptSample(100, TrackingCapability.All);

        Assert.False(module.TryAcceptSample(100, TrackingCapability.All));
    }

    [Fact]
    public void OlderSampleIsRejected()
    {
        ModuleRuntimeInfo module = NewModule();
        module.TryAcceptSample(100, TrackingCapability.All);

        Assert.False(module.TryAcceptSample(99, TrackingCapability.All));
    }

    [Fact]
    public void RepeatedSampleWithChangedCapabilitiesIsAcceptedOnce()
    {
        ModuleRuntimeInfo module = NewModule();
        module.TryAcceptSample(100, TrackingCapability.Eyes);

        Assert.True(module.TryAcceptSample(100, TrackingCapability.Eyes | TrackingCapability.Mouth));
        Assert.False(module.TryAcceptSample(100, TrackingCapability.Eyes | TrackingCapability.Mouth));
    }

    [Fact]
    public void UnstampedSampleIsAlwaysAccepted()
    {
        ModuleRuntimeInfo module = NewModule();
        module.TryAcceptSample(100, TrackingCapability.All);

        Assert.True(module.TryAcceptSample(0, TrackingCapability.All));
        Assert.True(module.TryAcceptSample(0, TrackingCapability.All));
    }

    [Fact]
    public void ModulesTrackSamplesIndependently()
    {
        ModuleRuntimeInfo first = NewModule();
        ModuleRuntimeInfo second = NewModule();
        first.TryAcceptSample(200, TrackingCapability.Eyes);

        Assert.True(second.TryAcceptSample(150, TrackingCapability.Mouth));
    }

    [Fact]
    public void RestartedModuleAcceptsItsFirstSample()
    {
        ModuleRuntimeInfo before = NewModule();
        before.TryAcceptSample(500, TrackingCapability.All);
        ModuleRuntimeInfo after = NewModule();

        Assert.True(after.TryAcceptSample(501, TrackingCapability.All));
    }
}
