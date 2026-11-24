using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Perf.Tests;

public sealed class CameraIpcTests
{
    private const string ModuleName = "Camera Fake";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class NoModules : IModuleDataService
    {
        public IEnumerable<InstallableTrackingModule> GetInstalledModules() => [];

        public IEnumerable<InstallableTrackingModule> GetLegacyModules() => [];

        public Task<IEnumerable<InstallableTrackingModule>> GetRemoteModulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingControls : IModuleCameraControl
    {
        public CameraControlCapabilities SupportedCameraControls => CameraControlCapabilities.Recenter | CameraControlCapabilities.Exposure;

        public ConcurrentQueue<CameraControlCommand> Applied { get; } = new();

        public void ApplyCameraControl(CameraControlCommand command) => Applied.Enqueue(command);
    }

    private sealed class FakeModule : IDisposable
    {
        private readonly VrcftSandboxClient _client;
        private readonly CameraControlInbox _inbox = new();
        private int _markers;
        private volatile bool _imageStream;

        public FakeModule(int hostPort, int processId)
        {
            _client = new VrcftSandboxClient(hostPort, NullLoggerFactory.Instance)
            {
                OnPacketReceivedCallback = OnPacket,
            };
            byte[] handshake = new HandshakePacket { ModulePath = "CameraFake.dll" }.GetBytes();
            BitConverter.TryWriteBytes(handshake.AsSpan(13), processId);
            _client.SendData(new EncodedIpcPacket(IpcPacket.PacketType.Handshake, handshake));
        }

        public RecordingControls Controls { get; } = new();

        public bool ImageStream => _imageStream;

        public void Send(IpcPacket packet) => _client.SendData(packet);

        public void SendFrame(string id, CameraStreamRole role, int width = 4, int height = 2) =>
            Send(new EncodedIpcPacket(IpcPacket.PacketType.DebugStreamFrameV2, Frame(id, role, width, height).GetBytes()));

        public string Marker()
        {
            string marker = "camera-ipc-marker-" + Interlocked.Increment(ref _markers);
            Send(new EventLogPacket(LogLevel.Information, marker));
            return marker;
        }

        public void Dispose() => _client.Dispose();

        private void OnPacket(in IpcPacket packet)
        {
            switch (packet)
            {
                case EventInitGetSupported:
                    _client.SendData(new ReplySupportedPacket { EyeAvailable = true, ExpressionAvailable = false });
                    break;
                case EventInitPacket:
                    _client.SendData(new ReplyInitPacket { EyeSuccess = true, ExpressionSuccess = false, ModuleInformationName = ModuleName });
                    break;
                case EventSetImageStreamPacket stream:
                    _imageStream = stream.Enabled;
                    break;
                case CameraControlPacket control:
                    _inbox.Enqueue(control.Command, Controls);
                    _inbox.Drain(Controls);
                    break;
                default:
                    break;
            }
        }
    }

    private static ImageFrameV2Packet Frame(string id, CameraStreamRole role, int width, int height) =>
        new(id, id + " label", role, CameraPixelFormat.Gray8, width, height, CameraControlCapabilities.Recenter, new byte[width * height]);

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(Patience.TotalSeconds * Stopwatch.Frequency);
        while (!condition())
        {
            Assert.True(Stopwatch.GetTimestamp() < deadline, "timed out waiting for " + what);
            await Task.Delay(10);
        }
    }

    private static Process StartStandIn() => Process.Start(new ProcessStartInfo
    {
        FileName = OperatingSystem.IsWindows() ? "ping" : "sleep",
        Arguments = OperatingSystem.IsWindows() ? "-n 120 127.0.0.1" : "120",
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
    }) ?? throw new InvalidOperationException("stand-in process didn't start");

    private static async Task RunAsync(Func<UnifiedLibManager, FakeModule, CapturingLoggerProvider, Task> body)
    {
        using Process standIn = StartStandIn();
        standIn.BeginOutputReadLine();
        using var logs = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([logs]);
        var manager = new UnifiedLibManager(factory, new InlineDispatcher(), new NoModules(), new NullSettings(), new LogLevelGate(), TimeProvider.System);
        FakeModule? module = null;
        try
        {
            manager.Initialize();
            await UntilAsync(() => manager.LoadedModulesMetadata.Count == 1 && manager.LoadedModulesMetadata[0].IsPlaceholder, "the empty module list");
            module = new FakeModule(UnifiedLibManager.SandboxPort, standIn.Id);
            await UntilAsync(() => manager.LoadedModulesMetadata.Any(m => string.Equals(m.Name, ModuleName, StringComparison.Ordinal) && m.Active), "the fake module to start");
            await body(manager, module, logs);
        }
        finally
        {
            try
            {
                manager.SetImageStreamEnabled(enabled: false);
                manager.TeardownAllAndReset();
                UnifiedLibManager.ShutdownSandboxServer();
                module?.Dispose();
            }
            finally
            {
                if (!standIn.HasExited)
                {
                    standIn.Kill();
                    await Task.WhenAny(standIn.WaitForExitAsync(CancellationToken.None), Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
                }
            }
        }
    }

    [Fact]
    public Task FramesAreIgnoredWhileTheImageStreamIsOff() => RunAsync(async (manager, module, logs) =>
    {
        module.SendFrame("left", CameraStreamRole.EyeLeft);
        string marker = module.Marker();
        await UntilAsync(() => logs.Saw(marker), "the module log marker");

        Assert.Empty(manager.CameraStreams.Snapshot());
        Assert.False(module.ImageStream);
    });

    [Fact]
    public Task EnabledStreamsLandInTheRegistryWithTheirRoleAndDetails() => RunAsync(async (manager, module, logs) =>
    {
        manager.SetImageStreamEnabled(enabled: true);
        await UntilAsync(() => module.ImageStream, "the image stream switch");

        module.SendFrame("left", CameraStreamRole.EyeLeft, width: 6, height: 3);
        module.SendFrame("room", CameraStreamRole.Passthrough);
        module.SendFrame("mouth", CameraStreamRole.Mouth);
        string marker = module.Marker();
        await UntilAsync(() => logs.Saw(marker), "the module log marker");

        IReadOnlyList<CameraStreamSnapshot> streams = manager.CameraStreams.Snapshot();
        Assert.Equal(["left", "mouth", "room"], streams.Select(s => s.StreamId).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        CameraStreamSnapshot left = streams.Single(s => string.Equals(s.StreamId, "left", StringComparison.Ordinal));
        Assert.Equal(ModuleName, left.ModuleName);
        Assert.Equal("left label", left.Label);
        Assert.Equal(CameraStreamRole.EyeLeft, left.Role);
        Assert.Equal(CameraPixelFormat.Gray8, left.Format);
        Assert.Equal((6, 3), (left.Width, left.Height));
        Assert.Equal(CameraControlCapabilities.Recenter, left.Capabilities);
        Assert.Equal(18, left.Data.Length);
        Assert.Equal(CameraStreamRole.Passthrough, streams.Single(s => string.Equals(s.StreamId, "room", StringComparison.Ordinal)).Role);
        Assert.Equal(CameraStreamRole.Mouth, streams.Single(s => string.Equals(s.StreamId, "mouth", StringComparison.Ordinal)).Role);
    });

    [Fact]
    public Task StreamsForAGroupTheModuleDoesNotOwnAreDropped() => RunAsync(async (manager, module, logs) =>
    {
        ModuleMetadataInternal info = manager.LoadedModulesMetadata.Single(m => string.Equals(m.Name, ModuleName, StringComparison.Ordinal));
        info.AllowedCapabilities = TrackingCapability.All & ~TrackingCapability.Mouth;
        await UntilAsync(() => !info.EffectiveCapabilities.Allows(TrackingCapability.Mouth), "mouth to be taken away");
        manager.SetImageStreamEnabled(enabled: true);
        await UntilAsync(() => module.ImageStream, "the image stream switch");

        module.SendFrame("mouth", CameraStreamRole.Mouth);
        module.SendFrame("right", CameraStreamRole.EyeRight);
        string marker = module.Marker();
        await UntilAsync(() => logs.Saw(marker), "the module log marker");

        Assert.Equal("right", Assert.Single(manager.CameraStreams.Snapshot()).StreamId);
    });

    [Fact]
    public Task MalformedFramesNeverReachTheRegistry() => RunAsync(async (manager, module, logs) =>
    {
        manager.SetImageStreamEnabled(enabled: true);
        await UntilAsync(() => module.ImageStream, "the image stream switch");

        byte[] zeroWidth = Frame("zero", CameraStreamRole.EyeLeft, 4, 2).GetBytes();
        BitConverter.TryWriteBytes(zeroWidth.AsSpan(10), 0);
        byte[] tooTall = Frame("tall", CameraStreamRole.EyeLeft, 4, 2).GetBytes();
        BitConverter.TryWriteBytes(tooTall.AsSpan(14), ImageFrameV2Packet.MaxDimension + 1);
        byte[] shortData = Frame("short", CameraStreamRole.EyeLeft, 4, 2).GetBytes()[..^3];
        byte[] badRole = Frame("role", CameraStreamRole.EyeLeft, 4, 2).GetBytes();
        badRole[8] = 200;
        byte[][] malformed = [zeroWidth, tooTall, shortData, badRole];
        foreach (byte[] bytes in malformed)
        {
            module.Send(new EncodedIpcPacket(IpcPacket.PacketType.DebugStreamFrameV2, bytes));
        }

        module.SendFrame("good", CameraStreamRole.EyeRight);
        string marker = module.Marker();
        await UntilAsync(() => logs.Saw(marker), "the module log marker");

        Assert.Equal("good", Assert.Single(manager.CameraStreams.Snapshot()).StreamId);
    });

    [Fact]
    public Task CameraControlsTravelFromTheHostToTheModule() => RunAsync(async (manager, module, logs) =>
    {
        manager.SetImageStreamEnabled(enabled: true);
        await UntilAsync(() => module.ImageStream, "the image stream switch");
        module.SendFrame("left", CameraStreamRole.EyeLeft);
        await UntilAsync(() => manager.CameraStreams.Snapshot().Count == 1, "the first frame");
        Guid moduleId = manager.CameraStreams.Snapshot()[0].ModuleId;
        var recenter = new CameraControlCommand("left", CameraControlKind.Recenter);
        var exposure = new CameraControlCommand("left", CameraControlKind.Exposure, 0.25f);
        var roi = new CameraControlCommand("left", CameraControlKind.Roi, 0f, 0.1f, 0.2f, 0.3f, 0.4f);

        Assert.True(manager.SendCameraControl(moduleId, recenter));
        Assert.True(manager.SendCameraControl(moduleId, exposure));
        Assert.True(manager.SendCameraControl(moduleId, roi));
        Assert.False(manager.SendCameraControl(Guid.NewGuid(), recenter));
        await UntilAsync(() => module.Controls.Applied.Count >= 2, "the controls to arrive");
        await Task.Delay(100);

        Assert.Equal([recenter, exposure], module.Controls.Applied);
    });
}
