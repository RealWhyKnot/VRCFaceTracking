using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public sealed class ParameterSenderServiceTests
{
    private const string PingAddress = "/vrcft/test/ping";

    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConnected
        {
            get; set;
        }
        public int InPort
        {
            get; set;
        }
        public string? DestinationAddress { get; set; } = "127.0.0.1";

        public int OutPort
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutPort)));
            }
        }
    }

    private sealed class NullSettings : ILocalSettingsService
    {
        public Task<T?> ReadSettingAsync<T>(string key, T? defaultValue, bool forceLocal, CancellationToken cancellationToken) => Task.FromResult(defaultValue);
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(object target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InlineDispatcher : IDispatcherService
    {
        public void Run(Action action) => action();
    }

    internal sealed class Harness : IAsyncDisposable
    {
        public readonly Socket Listener = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        public readonly ParameterSenderService Sender;
        private readonly EventHandler<TrackingDataEventArgs> _handler;

        public Harness(EventHandler<TrackingDataEventArgs> handler, TimerResolutionHold? timerResolution = null, SignalCapabilityTracker? capabilities = null, IExternalTrackingSource? external = null)
        {
            Listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Listener.ReceiveTimeout = 1000;
            var target = new FakeOscTarget();
            var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
            target.OutPort = ((IPEndPoint)Listener.LocalEndPoint!).Port;
            var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher(), external);
            Sender = new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance, timerResolution ?? new TimerResolutionHold(NullLogger.Instance), capabilities: capabilities ?? new SignalCapabilityTracker());
            _handler = handler;
            TrackingOutput.DataUpdated += _handler;
        }

        public bool WaitForPing(out double elapsedMs, long since)
        {
            byte[] buffer = new byte[65536];
            byte[] ping = Encoding.ASCII.GetBytes(PingAddress);
            while (true)
            {
                int length;
                try
                {
                    length = Listener.Receive(buffer);
                }
                catch (SocketException)
                {
                    elapsedMs = double.NaN;
                    return false;
                }
                if (buffer.AsSpan(0, length).IndexOf(ping) >= 0)
                {
                    elapsedMs = Stopwatch.GetElapsedTime(since).TotalMilliseconds;
                    return true;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            TrackingOutput.DataUpdated -= _handler;
            await Sender.StopAsync(CancellationToken.None);
            Sender.Dispose();
            Listener.Dispose();
        }
    }

    private static OscMessage Ping()
    {
        var message = new OscMessage(PingAddress, typeof(float))
        {
            Value = 1f,
        };
        return message;
    }

    [Fact]
    public async Task MarkDataUpdatedSendsWithoutWaitingForATick()
    {
        int armed = 0;
        int refreshes = 0;
        OscMessage ping = Ping();
        await using var harness = new Harness((_, _) =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
            {
                ParameterSenderService.Enqueue(ping);
            }

            Interlocked.Increment(ref refreshes);
        });
        await harness.Sender.StartAsync(CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref refreshes) > 0, TimeSpan.FromSeconds(2)), "the sender never ran an idle refresh");

        long start = Stopwatch.GetTimestamp();
        Volatile.Write(ref armed, 1);
        UnifiedTracking.MarkDataUpdated();

        Assert.True(harness.WaitForPing(out double elapsedMs, start));
        Assert.True(elapsedMs < 150, $"took {elapsedMs:N2} ms, the next idle refresh was 250 ms away");
    }

    [Fact]
    public async Task NoNewDataRefreshStillRunsUpdateData()
    {
        int calls = 0;
        await using var harness = new Harness((_, _) => Interlocked.Increment(ref calls));
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        int before = Volatile.Read(ref calls);
        var watch = Stopwatch.StartNew();
        while (Volatile.Read(ref calls) - before < 4 && watch.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(25);
        }

        int refreshes = Volatile.Read(ref calls) - before;
        long elapsedMs = watch.ElapsedMilliseconds;
        Assert.True(refreshes >= 4, $"{refreshes} refreshes in {elapsedMs} ms");
        Assert.True(refreshes <= (elapsedMs * 40 / 250) + 4, $"{refreshes} refreshes in {elapsedMs} ms");
    }

    [Fact]
    public async Task BurstOfMarksCoalesces()
    {
        int newVersions = 0;
        int lastSeen = UnifiedTracking.DataVersion;
        await using var harness = new Harness((_, _) =>
        {
            int version = UnifiedTracking.DataVersion;
            if (version != lastSeen)
            {
                lastSeen = version;
                Interlocked.Increment(ref newVersions);
            }
        });
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        Volatile.Write(ref newVersions, 0);
        for (int i = 0; i < 50; i++)
        {
            UnifiedTracking.MarkDataUpdated();
        }
        await Task.Delay(100);

        Assert.InRange(Volatile.Read(ref newVersions), 1, 3);
    }

    [Fact]
    public async Task StopReleasesTheTimerResolutionItRaised()
    {
        int raises = 0;
        int releases = 0;
        var hold = new TimerResolutionHold(NullLogger.Instance, () =>
        {
            Interlocked.Increment(ref raises);
            return true;
        }, () => Interlocked.Increment(ref releases));
        var harness = new Harness((_, _) => { }, hold);
        await harness.Sender.StartAsync(CancellationToken.None);

        UnifiedTracking.MarkDataUpdated();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref raises) > 0, TimeSpan.FromSeconds(2)), "the sender never raised the timer resolution");
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref raises));
        Assert.Equal(0, Volatile.Read(ref releases));

        await harness.DisposeAsync();
        Assert.Equal(1, Volatile.Read(ref releases));
    }

    [Fact]
    public async Task StartWithoutDataDoesNotRaiseTheTimerResolution()
    {
        int raises = 0;
        var hold = new TimerResolutionHold(NullLogger.Instance, () =>
        {
            Interlocked.Increment(ref raises);
            return true;
        }, () => { });
        await using var harness = new Harness((_, _) => { }, hold);
        await harness.Sender.StartAsync(CancellationToken.None);

        await Task.Delay(400);

        Assert.Equal(0, Volatile.Read(ref raises));
    }

    [Fact]
    public async Task NoNewDataSenderSleepsBetweenRefreshes()
    {
        await using var harness = new Harness((_, _) => { });
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        int wakes = harness.Sender.Wakes;
        await Task.Delay(1000);

        Assert.InRange(harness.Sender.Wakes - wakes, 1, 8);
    }

    private static byte[] ExternalPacket(string address, float value)
    {
        List<byte> bytes = [];
        AddOscString(bytes, address);
        AddOscString(bytes, ",f");
        byte[] number = BitConverter.GetBytes(value);
        Array.Reverse(number);
        bytes.AddRange(number);
        return [.. bytes];
    }

    private static void AddOscString(List<byte> bytes, string text)
    {
        bytes.AddRange(Encoding.ASCII.GetBytes(text));
        bytes.Add(0);
        while (bytes.Count % 4 != 0)
        {
            bytes.Add(0);
        }
    }

    [Fact]
    public async Task ExternalInputWithoutAModuleKeepsTheActiveRate()
    {
        using var external = new ExternalOscInputService(NullLogger<ExternalOscInputService>.Instance, new ExternalOscInputSettings(), new FakeOscTarget(), new NullSettings(), capabilities: new SignalCapabilityTracker());
        await using var harness = new Harness((_, _) => { }, external: external);
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        byte[] packet = ExternalPacket("/jawOpen", 0.5f);
        for (int i = 0; i < 20; i++)
        {
            int before = harness.Sender.DataUpdates;
            external.Ingest(packet, packet.Length);
            Assert.True(SpinWait.SpinUntil(() => harness.Sender.DataUpdates > before, TimeSpan.FromSeconds(2)), $"packet {i} never reached the output");
        }

        int start = harness.Sender.DataUpdates;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
        {
            external.Ingest(packet, packet.Length);
            long next = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 1000);
            while (Stopwatch.GetTimestamp() < next)
            {
                Thread.SpinWait(50);
            }
        }

        int updates = harness.Sender.DataUpdates - start;
        double elapsedMs = watch.Elapsed.TotalMilliseconds;
        Assert.True(updates <= (int)(elapsedMs / 5) + 3, $"{updates} data updates in {elapsedMs:F0} ms");
    }

    [Fact]
    public async Task UnmappedExternalTrafficLetsTheSenderIdle()
    {
        using var external = new ExternalOscInputService(NullLogger<ExternalOscInputService>.Instance, new ExternalOscInputSettings(), new FakeOscTarget(), new NullSettings(), capabilities: new SignalCapabilityTracker());
        await using var harness = new Harness((_, _) => { }, external: external);
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        byte[] packet = ExternalPacket("/avatar/parameters/NotAShape", 0.5f);
        int before = harness.Sender.DataUpdates;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 300)
        {
            Assert.False(external.Ingest(packet, packet.Length));
            await Task.Delay(10);
        }

        Assert.Equal(before, harness.Sender.DataUpdates);
    }

    [Fact]
    public async Task StopAsyncReturnsPromptly()
    {
        var harness = new Harness((_, _) => { });
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        long start = Stopwatch.GetTimestamp();
        await harness.DisposeAsync();

        Assert.True(Stopwatch.GetElapsedTime(start).TotalMilliseconds < 1000);
    }
}
