using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

public sealed class CameraFrameForwarderTests
{
    private sealed class Provider : ICameraStreamProvider
    {
        public List<CameraFrame> Frames { get; } = [];

        public List<CameraStreamDescriptor> Streams { get; } = [];

        public IReadOnlyList<CameraStreamDescriptor> GetCameraStreams() => Streams;

        public IReadOnlyList<CameraFrame> GetCameraFrames() => Frames;

        public void Show(params string[] ids)
        {
            Frames.Clear();
            foreach (string id in ids)
            {
                Frames.Add(Frame(id));
            }
        }
    }

    private readonly ManualClock _clock = new();
    private readonly Provider _provider = new();
    private readonly List<IpcPacket> _sent = [];

    private static CameraFrame Frame(string id) => new(id, CameraStreamRole.Other, id, 4, 4, CameraPixelFormat.Gray8, new byte[16]);

    private static string[] Ids(int from, int count) => [.. Enumerable.Range(from, count).Select(i => "cam" + i)];

    private CameraFrameForwarder Create() => new(_provider, CameraControlCapabilities.None, _sent.Add, _clock);

    private List<string> SentIds()
    {
        List<string> ids = [];
        foreach (IpcPacket packet in _sent)
        {
            Assert.True(VrcftPacketDecoder.TryDecodePacket(packet.GetBytes(), out IpcPacket? decoded));
            ids.Add(Assert.IsType<ImageFrameV2Packet>(decoded).StreamId);
        }

        _sent.Clear();
        return ids;
    }

    private int TickAfter(int milliseconds, CameraFrameForwarder forwarder)
    {
        _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        return forwarder.Tick();
    }

    [Fact]
    public void EachStreamIsHeldToItsOwnFrameInterval()
    {
        _provider.Streams.Add(new CameraStreamDescriptor("slow", "Slow", CameraStreamRole.Other, CameraPixelFormat.Gray8, TargetFrameIntervalMs: 100));
        _provider.Show("slow", "default");
        CameraFrameForwarder forwarder = Create();

        Assert.Equal(2, forwarder.Tick());
        Assert.Equal(["slow", "default"], SentIds());

        Assert.Equal(0, TickAfter(20, forwarder));
        Assert.Equal(1, TickAfter(20, forwarder));
        Assert.Equal(["default"], SentIds());
        Assert.Equal(0, TickAfter(20, forwarder));
        Assert.Equal(2, TickAfter(40, forwarder));
        Assert.Equal(["slow", "default"], SentIds());
    }

    [Fact]
    public void PollsFasterThanTheMinimumIntervalAreSkipped()
    {
        _provider.Streams.Add(new CameraStreamDescriptor("fast", "Fast", CameraStreamRole.Other, CameraPixelFormat.Gray8, TargetFrameIntervalMs: 1));
        _provider.Show("fast");
        CameraFrameForwarder forwarder = Create();

        Assert.Equal(1, forwarder.Tick());
        Assert.Equal(0, TickAfter(CameraFrameForwarder.MinIntervalMs - 6, forwarder));
        Assert.Equal(1, TickAfter(6, forwarder));
        Assert.Equal(2, SentIds().Count);
    }

    [Fact]
    public void NoMoreThanSixStreamsAreForwardedAtOnce()
    {
        _provider.Show(Ids(1, CameraStreamRegistry.MaxStreams + 2));
        CameraFrameForwarder forwarder = Create();

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(CameraStreamRegistry.MaxStreams, TickAfter(100, forwarder));
            Assert.Equal(Ids(1, CameraStreamRegistry.MaxStreams), SentIds(), StringComparer.Ordinal);
        }
    }

    [Fact]
    public void StreamsThatStopFreeTheirSlotForNewIds()
    {
        _provider.Show(Ids(1, CameraStreamRegistry.MaxStreams));
        CameraFrameForwarder forwarder = Create();
        Assert.Equal(CameraStreamRegistry.MaxStreams, forwarder.Tick());
        SentIds();

        _provider.Show(Ids(10, CameraStreamRegistry.MaxStreams));

        Assert.Equal(CameraStreamRegistry.MaxStreams, TickAfter(100, forwarder));
        Assert.Equal(Ids(10, CameraStreamRegistry.MaxStreams), SentIds(), StringComparer.Ordinal);
    }

    [Fact]
    public void AReconnectedCameraWithANewIdIsForwarded()
    {
        _provider.Show(Ids(1, CameraStreamRegistry.MaxStreams));
        CameraFrameForwarder forwarder = Create();
        Assert.Equal(CameraStreamRegistry.MaxStreams, forwarder.Tick());
        SentIds();

        string[] reconnected = [.. Ids(2, CameraStreamRegistry.MaxStreams - 1), "cam1-again"];
        _provider.Show(reconnected);

        Assert.Equal(CameraStreamRegistry.MaxStreams, TickAfter(100, forwarder));
        Assert.Equal(reconnected, SentIds(), StringComparer.Ordinal);
    }

    [Fact]
    public void ListedStreamsThatGoQuietAreDroppedOnceStale()
    {
        foreach (string id in Ids(1, CameraStreamRegistry.MaxStreams))
        {
            _provider.Streams.Add(new CameraStreamDescriptor(id, id, CameraStreamRole.Other, CameraPixelFormat.Gray8));
        }

        _provider.Show(Ids(1, CameraStreamRegistry.MaxStreams));
        CameraFrameForwarder forwarder = Create();
        Assert.Equal(CameraStreamRegistry.MaxStreams, forwarder.Tick());
        SentIds();

        _provider.Show("late");
        Assert.Equal(0, TickAfter(100, forwarder));
        Assert.Equal(0, TickAfter((int)CameraStreamRegistry.StaleAfter.TotalMilliseconds - 200, forwarder));

        Assert.Equal(1, TickAfter(200, forwarder));
        Assert.Equal(["late"], SentIds());
    }

    [Fact]
    public void ActiveStreamsKeepTheirSlotsWhenANewIdKnocks()
    {
        _provider.Show(Ids(1, CameraStreamRegistry.MaxStreams));
        CameraFrameForwarder forwarder = Create();
        Assert.Equal(CameraStreamRegistry.MaxStreams, forwarder.Tick());
        SentIds();

        for (int i = 0; i < 80; i++)
        {
            _provider.Show([.. Ids(1, CameraStreamRegistry.MaxStreams), "extra"]);
            TickAfter(100, forwarder);
            Assert.DoesNotContain("extra", SentIds(), StringComparer.Ordinal);
        }
    }
}
