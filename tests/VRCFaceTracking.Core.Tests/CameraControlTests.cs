using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

[Collection("PartialPacketState")]
public sealed class CameraControlTests
{
    private sealed class RecordingModule : IModuleCameraControl
    {
        public RecordingModule(CameraControlCapabilities supported)
        {
            SupportedCameraControls = supported;
        }

        public CameraControlCapabilities SupportedCameraControls { get; }

        public List<CameraControlCommand> Applied { get; } = [];

        public int AppliedOnThread { get; private set; }

        public void ApplyCameraControl(CameraControlCommand command)
        {
            Applied.Add(command);
            AppliedOnThread = Environment.CurrentManagedThreadId;
        }
    }

    [Fact]
    public void CommandRoundTripsWithEveryField()
    {
        var command = new CameraControlCommand("right", CameraControlKind.Roi, 0.5f, 0.1f, 0.2f, 0.6f, 0.7f);
        byte[] bytes = new CameraControlPacket(command).GetBytes();

        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? packet));
        CameraControlPacket decoded = Assert.IsType<CameraControlPacket>(packet);
        Assert.Equal(command, decoded.Command);
        Assert.Equal(IpcPacket.PacketType.EventCameraControl, decoded.GetPacketType());
    }

    [Theory]
    [InlineData(CameraControlKind.Exposure)]
    [InlineData(CameraControlKind.Brightness)]
    [InlineData(CameraControlKind.Roi)]
    [InlineData(CameraControlKind.Recalibrate)]
    [InlineData(CameraControlKind.Recenter)]
    public void EveryKindSurvivesTheWire(CameraControlKind kind)
    {
        byte[] bytes = new CameraControlPacket(new CameraControlCommand("s", kind)).GetBytes();
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? packet));
        Assert.Equal(kind, Assert.IsType<CameraControlPacket>(packet).Command.Kind);
    }

    [Fact]
    public void NonFiniteValuesAreRejected()
    {
        byte[] bytes = new CameraControlPacket(new CameraControlCommand("s", CameraControlKind.Exposure, 0.5f)).GetBytes();
        BitConverter.TryWriteBytes(bytes.AsSpan(9), float.NaN);

        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
    }

    [Fact]
    public void UnknownKindAndTruncatedPacketsAreRejected()
    {
        byte[] bytes = new CameraControlPacket(new CameraControlCommand("stream", CameraControlKind.Recenter)).GetBytes();

        byte[] unknown = [.. bytes];
        unknown[8] = 99;
        Assert.False(VrcftPacketDecoder.TryDecodePacket(unknown, out _));
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes[..^2], out _));
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes[..12], out _));
    }

    [Fact]
    public void InboxHoldsCommandsUntilTheModuleThreadDrains()
    {
        var inbox = new CameraControlInbox();
        var module = new RecordingModule(CameraControlCapabilities.Recenter | CameraControlCapabilities.Exposure);
        var recenter = new CameraControlCommand("a", CameraControlKind.Recenter);
        var exposure = new CameraControlCommand("a", CameraControlKind.Exposure, 0.4f);

        Assert.True(inbox.Enqueue(recenter, module));
        Assert.True(inbox.Enqueue(exposure, module));
        Assert.Empty(module.Applied);
        Assert.Equal(2, inbox.Count);

        int applied = 0;
        var worker = new Thread(() => applied = inbox.Drain(module));
        worker.Start();
        worker.Join();

        Assert.Equal(2, applied);
        Assert.Equal([recenter, exposure], module.Applied);
        Assert.NotEqual(Environment.CurrentManagedThreadId, module.AppliedOnThread);
        Assert.Equal(0, inbox.Count);
    }

    [Fact]
    public void InboxRejectsUnsupportedCommandsAndModulesWithoutControl()
    {
        var inbox = new CameraControlInbox();
        var module = new RecordingModule(CameraControlCapabilities.Recenter);

        Assert.False(inbox.Enqueue(new CameraControlCommand("a", CameraControlKind.Recalibrate), module));
        Assert.False(inbox.Enqueue(new CameraControlCommand("a", CameraControlKind.Recenter), module: null));
        Assert.Equal(0, inbox.Count);
        Assert.Equal(0, inbox.Drain(module));
    }

    [Fact]
    public void InboxIsBounded()
    {
        var inbox = new CameraControlInbox();
        var module = new RecordingModule(CameraControlCapabilities.Recenter);
        var command = new CameraControlCommand("a", CameraControlKind.Recenter);

        for (int i = 0; i < CameraControlInbox.MaxPending; i++)
        {
            Assert.True(inbox.Enqueue(command, module));
        }

        Assert.False(inbox.Enqueue(command, module));
        Assert.Equal(CameraControlInbox.MaxPending, inbox.Drain(module));
        Assert.True(inbox.Enqueue(command, module));
    }

    [Theory]
    [InlineData(CameraControlCapabilities.None, CameraControlKind.Recenter, false)]
    [InlineData(CameraControlCapabilities.Recenter, CameraControlKind.Recenter, true)]
    [InlineData(CameraControlCapabilities.Recalibrate, CameraControlKind.Recenter, false)]
    [InlineData(CameraControlCapabilities.Roi, CameraControlKind.Roi, true)]
    [InlineData(CameraControlCapabilities.Brightness | CameraControlCapabilities.Exposure, CameraControlKind.Brightness, true)]
    public void SupportIsDecidedByTheMatchingFlag(CameraControlCapabilities capabilities, CameraControlKind kind, bool expected)
        => Assert.Equal(expected, CameraControlInbox.IsSupported(capabilities, kind));
}
