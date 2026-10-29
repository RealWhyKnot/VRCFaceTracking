using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Tests;

public sealed class EventStatusUpdatePacketTests
{
    [Theory]
    [InlineData(ModuleState.Active, true, false)]
    [InlineData(ModuleState.Idle, false, true)]
    [InlineData(ModuleState.Uninitialized, true, true)]
    public void RoundTripPreservesStateAndUsageFlags(ModuleState state, bool usingEye, bool usingExpression)
    {
        var packet = new EventStatusUpdatePacket
        {
            ModuleState = state,
            UsingEye = usingEye,
            UsingExpression = usingExpression,
        };

        byte[] bytes = packet.GetBytes();
        Assert.Equal(14, bytes.Length);

        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? decoded));
        EventStatusUpdatePacket decodedPacket = Assert.IsType<EventStatusUpdatePacket>(decoded);
        Assert.Equal(state, decodedPacket.ModuleState);
        Assert.Equal(usingEye, decodedPacket.UsingEye);
        Assert.Equal(usingExpression, decodedPacket.UsingExpression);
    }

    [Fact]
    public void DecodeLegacyTwelveBytePacketDefaultsFlagsToFalse()
    {
        var packet = new EventStatusUpdatePacket();
        packet.Decode(new EventStatusUpdatePacket { ModuleState = ModuleState.Active }.GetBytes()[..12]);
        Assert.Equal(ModuleState.Active, packet.ModuleState);
        Assert.False(packet.UsingEye);
        Assert.False(packet.UsingExpression);
    }
}
