using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Perf.Tests;

public sealed class CameraLifecycleTests
{
    private sealed class NoModules : IModuleDataService
    {
        public IEnumerable<InstallableTrackingModule> GetInstalledModules() => [];

        public IEnumerable<InstallableTrackingModule> GetLegacyModules() => [];

        public Task<IEnumerable<InstallableTrackingModule>> GetRemoteModulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static UnifiedLibManager Create() =>
        new(NullLoggerFactory.Instance, new InlineDispatcher(), new NoModules(), new NullSettings(), new LogLevelGate(), TimeProvider.System);

    [Fact]
    public void DisablingTheImageStreamClearsEveryStream()
    {
        UnifiedLibManager manager = Create();
        var module = Guid.NewGuid();
        manager.CameraStreams.Update(module, "Module", new ImageFrameV2Packet("left", "Left", CameraStreamRole.EyeLeft, CameraPixelFormat.Gray8, 4, 4, CameraControlCapabilities.None, new byte[16]));
        manager.CameraStreams.UpdateLegacy(module, "Module", ImageFrameUpdatePacket.EyeKind, 4, 4, new byte[64]);
        Assert.Equal(2, manager.CameraStreams.Snapshot().Count);

        manager.SetImageStreamEnabled(enabled: false);

        Assert.Empty(manager.CameraStreams.Snapshot());
    }

    [Fact]
    public void ControlsCannotReachAModuleThatIsNotRunning()
    {
        UnifiedLibManager manager = Create();

        Assert.False(manager.SendCameraControl(Guid.NewGuid(), new CameraControlCommand("left", CameraControlKind.Recenter)));
    }
}
