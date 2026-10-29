using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Tests;

public sealed class EventSetVerbosePacketTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTripsThroughDecoder(bool verbose)
    {
        byte[] bytes = new EventSetVerbosePacket { Verbose = verbose }.GetBytes();
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? decoded));
        EventSetVerbosePacket packet = Assert.IsType<EventSetVerbosePacket>(decoded);
        Assert.Equal(verbose, packet.Verbose);
    }
}
