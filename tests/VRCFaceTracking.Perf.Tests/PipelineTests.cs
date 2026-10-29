using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using Xunit.Abstractions;

namespace VRCFaceTracking.Perf.Tests;

public sealed class PipelineTests
{
    private const int SampleHz = 90;
    private const double IdleSenderWakesPerSecondBudget = 6;
    private const double StreamingWakesPerSampleBudget = 1.3;
    private const double IdleCpuBudgetPercent = 2;
    private const double StreamingCpuBudgetPercent = 12;
    private const double StreamingAllocationBudgetKbPerSecond = 120;

    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private readonly ITestOutputHelper _output;

    public PipelineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class ModuleFeed : IDisposable
    {
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private readonly Thread _thread;
        private volatile bool _stop;
        private long _sent;

        public ModuleFeed(int hostPort)
        {
            _socket.Connect(new IPEndPoint(IPAddress.Loopback, hostPort));
            byte[][] wires = new byte[100][];
            for (int frame = 0; frame < wires.Length; frame++)
            {
                var data = new UnifiedTrackingData();
                for (int i = 0; i < data.Shapes.Length; i++)
                {
                    data.Shapes[i].Weight = frame / 100f;
                }
                wires[frame] = ReplyUpdatePacket.Capture(data, frame + 1).GetBytes();
            }

            _thread = new Thread(() =>
            {
                long next = Stopwatch.GetTimestamp();
                int frame = 0;
                while (!_stop)
                {
                    _socket.Send(wires[frame++ % wires.Length]);
                    Interlocked.Increment(ref _sent);

                    next += Stopwatch.Frequency / SampleHz;
                    long wait = next - Stopwatch.GetTimestamp();
                    if (wait > 0)
                    {
                        Thread.Sleep(TimeSpan.FromTicks(wait * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Synthetic module",
            };
            _thread.Start();
        }

        public long Sent => Interlocked.Read(ref _sent);

        public void Dispose()
        {
            _stop = true;
            _thread.Join(1000);
            _socket.Dispose();
        }
    }

    private static VrcftSandboxServer StartHost(Action onSample)
    {
        var server = new VrcftSandboxServer(NullLoggerFactory.Instance, []);
        server.OnPacketReceived += (in packet, in port) =>
        {
            if (packet is ReplyUpdatePacket reply)
            {
                using (UnifiedTracking.LockData())
                {
                    reply.UpdateGlobalState(TrackingCapability.All);
                }
                onSample();
            }
        };
        return server;
    }

    [Fact]
    public async Task IdleSenderWakesRarely()
    {
        await using var harness = new SenderHarness(allMutations: true);
        await harness.StartAsync();
        await Task.Delay(Settle);

        int wakes0 = harness.Sender.Wakes;
        long start = Stopwatch.GetTimestamp();
        await Task.Delay(Window);
        double perSecond = (harness.Sender.Wakes - wakes0) / Stopwatch.GetElapsedTime(start).TotalSeconds;

        _output.Report("idle sender", $"{perSecond:N1} wakes/s, budget {IdleSenderWakesPerSecondBudget}");
        Assert.True(perSecond <= IdleSenderWakesPerSecondBudget, $"{perSecond:N1} wakes/s with no tracking data");
    }

    [Fact]
    public async Task StreamingSenderWakesAboutOncePerSample()
    {
        await using var harness = new SenderHarness(allMutations: true, drainSink: true);
        long samples = 0L;
        using VrcftSandboxServer host = StartHost(() => Interlocked.Increment(ref samples));
        await harness.StartAsync();
        using var feed = new ModuleFeed(host.Port);
        await Task.Delay(Settle);

        int wakes0 = harness.Sender.Wakes;
        long samples0 = Interlocked.Read(ref samples);
        await Task.Delay(Window);
        int wakes = harness.Sender.Wakes - wakes0;
        long received = Interlocked.Read(ref samples) - samples0;
        double ratio = (double)wakes / Math.Max(1, received);

        _output.Report("streaming sender", $"{wakes} wakes for {received} samples ({ratio:N2} per sample), budget {StreamingWakesPerSampleBudget}");
        Assert.True(received > SampleHz * Window.TotalSeconds * 0.8, $"only {received} samples arrived");
        Assert.True(ratio <= StreamingWakesPerSampleBudget, $"{ratio:N2} sender wakes per sample");
    }

    [Fact]
    public async Task IdleHostUsesAlmostNoCpu()
    {
        await using var harness = new SenderHarness(allMutations: true);
        using VrcftSandboxServer host = StartHost(() => { });
        await harness.StartAsync();
        await Task.Delay(Settle);

        var self = Process.GetCurrentProcess();
        Dictionary<int, (string Name, double CpuMs)> threads0 = CpuClock.Threads(self);
        CpuSample sample = Measure.Process(Window);
        _output.Report("idle host threads", CpuClock.TopThreads(threads0, CpuClock.Threads(self), sample.ElapsedMs));

        _output.Report("idle host", $"{sample.PercentOfOneCore:N2}% of one core, {sample.AllocatedKbPerSecond:N1} KB/s allocated, budget {IdleCpuBudgetPercent}%");
        Assert.True(sample.PercentOfOneCore < IdleCpuBudgetPercent, $"{sample.PercentOfOneCore:N2}% of one core while idle");
    }

    [Fact]
    public async Task StreamingHostStaysWithinCpuAndAllocationBudget()
    {
        await using var harness = new SenderHarness(allMutations: true, drainSink: true);
        long samples = 0L;
        using VrcftSandboxServer host = StartHost(() => Interlocked.Increment(ref samples));
        await harness.StartAsync();
        using var feed = new ModuleFeed(host.Port);
        await Task.Delay(Settle);

        long datagrams0 = harness.Sink.Datagrams;
        long samples0 = Interlocked.Read(ref samples);
        var self = Process.GetCurrentProcess();
        Dictionary<int, (string Name, double CpuMs)> threads0 = CpuClock.Threads(self);
        CpuSample sample = Measure.Process(Window);
        _output.Report("streaming host threads", CpuClock.TopThreads(threads0, CpuClock.Threads(self), sample.ElapsedMs));
        long received = Interlocked.Read(ref samples) - samples0;
        long datagrams = harness.Sink.Datagrams - datagrams0;

        _output.Report("streaming host", $"{sample.PercentOfOneCore:N2}% of one core, {sample.AllocatedKbPerSecond:N1} KB/s, gc {sample.Gen0}/{sample.Gen1}/{sample.Gen2}, {received} samples in, {datagrams} OSC datagrams out, budget {StreamingCpuBudgetPercent}% / {StreamingAllocationBudgetKbPerSecond} KB/s");
        Assert.True(received > SampleHz * Window.TotalSeconds * 0.8, $"only {received} samples arrived");
        Assert.True(datagrams > 0, "no OSC went out");
        Assert.True(sample.PercentOfOneCore < StreamingCpuBudgetPercent, $"{sample.PercentOfOneCore:N2}% of one core while streaming");
        Assert.True(sample.AllocatedKbPerSecond < StreamingAllocationBudgetKbPerSecond, $"{sample.AllocatedKbPerSecond:N1} KB/s allocated while streaming");
        Assert.Equal(0, sample.Gen2);
    }
}
