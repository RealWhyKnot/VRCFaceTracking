using VRCFaceTracking.Core.Library;

namespace VRCFaceTracking.Core.Tests;

public class TrackingCapabilitiesTests
{
    [Fact]
    public void Allows_MatchesHasFlagForEveryCombination()
    {
        for (var allowed = 0; allowed <= (int)TrackingCapability.All; allowed++)
        {
            for (var capability = 0; capability <= (int)TrackingCapability.All; capability++)
            {
                var a = (TrackingCapability)allowed;
                var c = (TrackingCapability)capability;
                Assert.Equal(a.HasFlag(c), a.Allows(c));
            }
        }
    }

    [Fact]
    public void Allows_DoesNotAllocate()
    {
        var allowed = TrackingCapabilities.EyeHalf;
        var hits = 0;
        for (var i = 0; i < 100; i++)
        {
            hits += allowed.Allows(TrackingCapability.Eyes) ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            hits += allowed.Allows(TrackingCapability.Eyes) ? 1 : 0;
            hits += allowed.Allows(TrackingCapability.Mouth) ? 1 : 0;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1100, hits);
        Assert.Equal(0, allocated);
    }
}
