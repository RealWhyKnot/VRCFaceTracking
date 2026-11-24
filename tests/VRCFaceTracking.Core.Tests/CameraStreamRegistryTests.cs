using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

[Collection("PartialPacketState")]
public sealed class CameraStreamRegistryTests
{
    private static readonly Guid ModuleA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ModuleB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static ImageFrameV2Packet Frame(string id, CameraPixelFormat format = CameraPixelFormat.Gray8, int size = 4, CameraControlCapabilities capabilities = CameraControlCapabilities.None) =>
        new(id, id.ToUpperInvariant(), CameraStreamRole.EyeLeft, format, size, size, capabilities, new byte[format == CameraPixelFormat.Gray8 ? size * size : size * size * 4]);

    [Fact]
    public void StreamsAreKeyedByModuleAndStreamId()
    {
        var registry = new CameraStreamRegistry();

        Assert.True(registry.Update(ModuleA, "Module A", Frame("left")));
        Assert.True(registry.Update(ModuleA, "Module A", Frame("right")));
        Assert.True(registry.Update(ModuleB, "Module B", Frame("left")));
        Assert.True(registry.Update(ModuleA, "Module A", Frame("left")));

        IReadOnlyList<CameraStreamSnapshot> streams = registry.Snapshot();
        Assert.Equal(3, streams.Count);
        Assert.Equal(2, streams.Count(s => s.ModuleId == ModuleA));
        Assert.Single(streams, s => s.ModuleId == ModuleB && string.Equals(s.StreamId, "left", StringComparison.Ordinal));
    }

    [Fact]
    public void NewFramesReplaceOldOnesAndRaiseTheSequence()
    {
        var registry = new CameraStreamRegistry();
        registry.Update(ModuleA, "Module A", Frame("left", capabilities: CameraControlCapabilities.None));
        long first = registry.Snapshot()[0].Sequence;
        int version = registry.Version;

        registry.Update(ModuleA, "Module A", Frame("left", capabilities: CameraControlCapabilities.Recenter));

        CameraStreamSnapshot only = Assert.Single(registry.Snapshot());
        Assert.True(only.Sequence > first);
        Assert.Equal(CameraControlCapabilities.Recenter, only.Capabilities);
        Assert.True(registry.Version > version);
    }

    [Fact]
    public void AllFormatsAreStored()
    {
        var registry = new CameraStreamRegistry();
        registry.Update(ModuleA, "Module A", Frame("gray", CameraPixelFormat.Gray8));
        registry.Update(ModuleA, "Module A", Frame("rgba", CameraPixelFormat.Rgba8888));
        registry.Update(ModuleA, "Module A", Frame("bgra", CameraPixelFormat.Bgra8888));

        Assert.Equal(
            [CameraPixelFormat.Gray8, CameraPixelFormat.Rgba8888, CameraPixelFormat.Bgra8888],
            registry.Snapshot().Select(s => s.Format));
    }

    [Fact]
    public void StreamCountIsCapped()
    {
        var registry = new CameraStreamRegistry();
        for (int i = 0; i < CameraStreamRegistry.MaxStreams; i++)
        {
            Assert.True(registry.Update(ModuleA, "Module A", Frame("s" + i)));
        }

        Assert.False(registry.Update(ModuleA, "Module A", Frame("extra")));
        Assert.True(registry.Update(ModuleA, "Module A", Frame("s0")));
        Assert.Equal(CameraStreamRegistry.MaxStreams, registry.Snapshot().Count);
    }

    [Fact]
    public void StaleStreamsMakeRoomWhenTheRegistryIsFull()
    {
        var clock = new ManualClock();
        var registry = new CameraStreamRegistry(clock);
        for (int i = 0; i < CameraStreamRegistry.MaxStreams; i++)
        {
            registry.Update(ModuleA, "Module A", Frame("s" + i));
        }

        Assert.False(registry.Update(ModuleA, "Module A", Frame("extra")));

        clock.Advance(CameraStreamRegistry.StaleAfter + TimeSpan.FromSeconds(1));
        registry.Update(ModuleA, "Module A", Frame("s0"));

        Assert.True(registry.Update(ModuleA, "Module A", Frame("extra")));
        Assert.Equal(2, registry.Snapshot().Count);
    }

    [Fact]
    public void LegacyEyeAndLipFramesAreSynthesized()
    {
        var registry = new CameraStreamRegistry();

        Assert.True(registry.UpdateLegacy(ModuleA, "Module A", ImageFrameUpdatePacket.EyeKind, 4, 4, new byte[64]));
        Assert.True(registry.UpdateLegacy(ModuleA, "Module A", ImageFrameUpdatePacket.LipKind, 2, 2, new byte[16]));

        IReadOnlyList<CameraStreamSnapshot> streams = registry.Snapshot();
        CameraStreamSnapshot eye = Assert.Single(streams, s => string.Equals(s.StreamId, CameraStreamRegistry.LegacyEyeId, StringComparison.Ordinal));
        CameraStreamSnapshot lip = Assert.Single(streams, s => string.Equals(s.StreamId, CameraStreamRegistry.LegacyLipId, StringComparison.Ordinal));
        Assert.True(eye.IsLegacy);
        Assert.True(lip.IsLegacy);
        Assert.Equal(CameraStreamRole.EyeCombined, eye.Role);
        Assert.Equal(CameraStreamRole.Mouth, lip.Role);
        Assert.Equal(CameraPixelFormat.Rgba8888, eye.Format);
        Assert.Equal(CameraControlCapabilities.None, eye.Capabilities);
        Assert.Equal((4, 4), (eye.Width, eye.Height));
    }

    [Fact]
    public void BadFramesAreRefused()
    {
        var registry = new CameraStreamRegistry();

        Assert.False(registry.UpdateLegacy(ModuleA, "Module A", ImageFrameUpdatePacket.EyeKind, 4, 4, new byte[10]));
        Assert.False(registry.Update(ModuleA, "Module A", Frame(string.Empty)));
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void ClearingAModuleLeavesOthersAlone()
    {
        var registry = new CameraStreamRegistry();
        registry.Update(ModuleA, "Module A", Frame("left"));
        registry.Update(ModuleB, "Module B", Frame("left"));

        registry.ClearModule(ModuleA);

        Assert.Equal(ModuleB, Assert.Single(registry.Snapshot()).ModuleId);
        registry.ClearAll();
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task ConcurrentWritersAndReadersStayConsistent()
    {
        var registry = new CameraStreamRegistry();
        Task[] writers = [.. Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                registry.Update(ModuleA, "Module A", Frame("s" + (i % 4) + w));
                registry.Snapshot();
            }
        }))];

        await Task.WhenAll(writers);

        Assert.True(registry.Snapshot().Count <= CameraStreamRegistry.MaxStreams);
    }
}
