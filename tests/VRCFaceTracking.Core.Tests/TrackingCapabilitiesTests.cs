using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public sealed class TrackingCapabilitiesTests
{
    [Fact]
    public void AllowsMatchesHasFlagForEveryCombination()
    {
        for (int allowed = 0; allowed <= (int)TrackingCapability.All; allowed++)
        {
            for (int capability = 0; capability <= (int)TrackingCapability.All; capability++)
            {
                var a = (TrackingCapability)allowed;
                var c = (TrackingCapability)capability;
                Assert.Equal(a.HasFlag(c), a.Allows(c));
            }
        }
    }

    [Fact]
    public void AllowsDoesNotAllocate()
    {
        TrackingCapability allowed = TrackingCapabilities.EyeHalf;
        int hits = 0;
        for (int i = 0; i < 100; i++)
        {
            hits += allowed.Allows(TrackingCapability.Eyes) ? 1 : 0;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            hits += allowed.Allows(TrackingCapability.Eyes) ? 1 : 0;
            hits += allowed.Allows(TrackingCapability.Mouth) ? 1 : 0;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1100, hits);
        Assert.Equal(0, allocated);
    }
}
