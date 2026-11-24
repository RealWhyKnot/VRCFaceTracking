using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

public sealed class CameraModuleFaultTests
{
    private static readonly CameraFrame GoodFrame = new("left", CameraStreamRole.EyeLeft, "Left", 4, 4, CameraPixelFormat.Gray8, new byte[16]);

    private sealed class BrokenProvider : ICameraStreamProvider
    {
        public bool ThrowOnFrames { get; init; }

        public bool ThrowOnStreams { get; init; }

        public int FrameCalls { get; private set; }

        public int StreamCalls { get; private set; }

        public IReadOnlyList<CameraFrame> GetCameraFrames()
        {
            FrameCalls++;
            return ThrowOnFrames ? throw new InvalidOperationException("frames broke") : [GoodFrame];
        }

        public IReadOnlyList<CameraStreamDescriptor> GetCameraStreams()
        {
            StreamCalls++;
            return ThrowOnStreams ? throw new NotSupportedException("streams broke") : [];
        }
    }

    private sealed class BrokenControls : IModuleCameraControl
    {
        public bool ThrowOnSupported { get; init; }

        public int Applied { get; private set; }

        public CameraControlCapabilities SupportedCameraControls =>
            ThrowOnSupported ? throw new InvalidOperationException("supported broke") : CameraControlCapabilities.Recenter;

        public void ApplyCameraControl(CameraControlCommand command)
        {
            Applied++;
            throw new ArgumentException("apply broke", nameof(command));
        }
    }

    private static int Errors(RecordingLogger logger) => logger.Entries.Count(entry => entry.Level == LogLevel.Error);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ThrowingProviderIsLoggedOnceAndSwitchedOff(bool throwOnFrames, bool throwOnStreams)
    {
        var clock = new ManualClock();
        var logger = new RecordingLogger();
        var provider = new BrokenProvider { ThrowOnFrames = throwOnFrames, ThrowOnStreams = throwOnStreams };
        var sent = new List<IpcPacket>();
        var forwarder = new CameraFrameForwarder(provider, CameraControlCapabilities.None, sent.Add, clock, logger);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(0, forwarder.Tick());
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(forwarder.IsFaulted);
        Assert.Empty(sent);
        Assert.Equal(1, provider.FrameCalls);
        Assert.Equal(1, Errors(logger));
        (LogLevel _, string message, Exception? exception) = logger.Entries.Single();
        Assert.Contains(nameof(BrokenProvider), message, StringComparison.Ordinal);
        Assert.NotNull(exception);
    }

    [Fact]
    public void HealthyProviderKeepsForwarding()
    {
        var clock = new ManualClock();
        var logger = new RecordingLogger();
        var sent = new List<IpcPacket>();
        var forwarder = new CameraFrameForwarder(new BrokenProvider(), CameraControlCapabilities.None, sent.Add, clock, logger);

        Assert.Equal(1, forwarder.Tick());
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, forwarder.Tick());

        Assert.False(forwarder.IsFaulted);
        Assert.Equal(2, sent.Count);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void ThrowingApplyIsLoggedOnceAndControlsSwitchOff()
    {
        var logger = new RecordingLogger();
        var inbox = new CameraControlInbox();
        var module = new BrokenControls();
        var command = new CameraControlCommand("left", CameraControlKind.Recenter);
        Assert.True(inbox.Enqueue(command, module, logger));
        Assert.True(inbox.Enqueue(command, module, logger));

        Assert.Equal(0, inbox.Drain(module, logger));
        Assert.Equal(0, inbox.Drain(module, logger));

        Assert.True(inbox.IsFaulted);
        Assert.Equal(1, module.Applied);
        Assert.Equal(0, inbox.Count);
        Assert.False(inbox.Enqueue(command, module, logger));
        Assert.Equal(1, Errors(logger));
        Assert.Contains(nameof(IModuleCameraControl.ApplyCameraControl), logger.Entries[0].Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentException>(logger.Entries[0].Exception);
    }

    [Fact]
    public void ThrowingCapabilitiesAreTreatedAsNoControls()
    {
        var logger = new RecordingLogger();
        var inbox = new CameraControlInbox();
        var module = new BrokenControls { ThrowOnSupported = true };

        Assert.Equal(CameraControlCapabilities.None, inbox.SupportedBy(module, logger));
        Assert.False(inbox.Enqueue(new CameraControlCommand("left", CameraControlKind.Recenter), module, logger));
        Assert.Equal(CameraControlCapabilities.None, inbox.SupportedBy(module, logger));

        Assert.True(inbox.IsFaulted);
        Assert.Equal(1, Errors(logger));
        Assert.Contains(nameof(IModuleCameraControl.SupportedCameraControls), logger.Entries[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FaultFilterCatchesOrdinaryModuleErrors()
    {
        Assert.True(CameraControlInbox.IsModuleFault(new InvalidOperationException()));
        Assert.True(CameraControlInbox.IsModuleFault(new NotSupportedException()));
    }
}
