using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Tests;

public class EventStatusUpdatePacketTests
{
    [Theory]
    [InlineData(ModuleState.Active, true, false)]
    [InlineData(ModuleState.Idle, false, true)]
    [InlineData(ModuleState.Uninitialized, true, true)]
    public void RoundTrip_PreservesStateAndUsageFlags(ModuleState state, bool usingEye, bool usingExpression)
    {
        var packet = new EventStatusUpdatePacket
        {
            ModuleState = state,
            UsingEye = usingEye,
            UsingExpression = usingExpression,
        };

        var bytes = packet.GetBytes();
        Assert.Equal(14, bytes.Length);

        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out var decoded));
        var decodedPacket = Assert.IsType<EventStatusUpdatePacket>(decoded);
        Assert.Equal(state, decodedPacket.ModuleState);
        Assert.Equal(usingEye, decodedPacket.UsingEye);
        Assert.Equal(usingExpression, decodedPacket.UsingExpression);
    }

    [Fact]
    public void Decode_LegacyTwelveBytePacket_DefaultsFlagsToFalse()
    {
        var packet = new EventStatusUpdatePacket();
        packet.Decode(new EventStatusUpdatePacket { ModuleState = ModuleState.Active }.GetBytes()[..12]);
        Assert.Equal(ModuleState.Active, packet.ModuleState);
        Assert.False(packet.UsingEye);
        Assert.False(packet.UsingExpression);
    }
}
