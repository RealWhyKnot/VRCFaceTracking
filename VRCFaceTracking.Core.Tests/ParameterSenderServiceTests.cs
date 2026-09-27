using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public class ParameterSenderServiceTests
{
    private const string PingAddress = "/vrcft/test/ping";

    private sealed class FakeOscTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _outPort;

        public bool IsConnected
        {
            get; set;
        }
        public int InPort
        {
            get; set;
        }
        public string DestinationAddress { get; set; } = "127.0.0.1";

        public int OutPort
        {
            get => _outPort;
            set
            {
                _outPort = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutPort)));
            }
        }
    }

    private sealed class NullSettings : ILocalSettingsService
    {
        public Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) => Task.FromResult(defaultValue!);
        public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false) => Task.CompletedTask;
        public Task Save(object target) => Task.CompletedTask;
        public Task Load(object target) => Task.CompletedTask;
        public Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class InlineDispatcher : IDispatcherService
    {
        public void Run(Action action) => action();
    }

    private sealed class Harness : IAsyncDisposable
    {
        public readonly Socket Listener = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        public readonly ParameterSenderService Sender;
        private readonly Action<UnifiedTrackingData> _handler;

        public Harness(Action<UnifiedTrackingData> handler, TimerResolutionHold? timerResolution = null)
        {
            Listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Listener.ReceiveTimeout = 1000;
            var target = new FakeOscTarget();
            var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
            target.OutPort = ((IPEndPoint)Listener.LocalEndPoint!).Port;
            var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
            Sender = new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance, timerResolution ?? new TimerResolutionHold(NullLogger.Instance));
            _handler = handler;
            UnifiedTracking.OnUnifiedDataUpdated += _handler;
        }

        public bool WaitForPing(out double elapsedMs, long since)
        {
            var buffer = new byte[65536];
            var ping = Encoding.ASCII.GetBytes(PingAddress);
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
            UnifiedTracking.OnUnifiedDataUpdated -= _handler;
            await Sender.StopAsync(CancellationToken.None);
            Sender.Dispose();
            Listener.Dispose();
        }
    }

    private static OscMessage Ping()
    {
        var message = new OscMessage(PingAddress, typeof(float));
        message.Value = 1f;
        return message;
    }

    [Fact]
    public async Task MarkDataUpdated_SendsWithoutWaitingForATick()
    {
        var armed = 0;
        var ping = Ping();
        await using var harness = new Harness(_ =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
            {
                ParameterSenderService.Enqueue(ping);
            }
        });
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var start = Stopwatch.GetTimestamp();
        Volatile.Write(ref armed, 1);
        UnifiedTracking.MarkDataUpdated();

        Assert.True(harness.WaitForPing(out var elapsedMs, start));
        Assert.True(elapsedMs < 50, $"took {elapsedMs:N2} ms");
    }

    [Fact]
    public async Task NoNewData_RefreshStillRunsUpdateData()
    {
        var calls = 0;
        await using var harness = new Harness(_ => Interlocked.Increment(ref calls));
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var before = Volatile.Read(ref calls);
        await Task.Delay(250);
        var refreshes = Volatile.Read(ref calls) - before;

        Assert.InRange(refreshes, 4, 40);
    }

    [Fact]
    public async Task BurstOfMarks_Coalesces()
    {
        var newVersions = 0;
        var lastSeen = UnifiedTracking.DataVersion;
        await using var harness = new Harness(_ =>
        {
            var version = UnifiedTracking.DataVersion;
            if (version != lastSeen)
            {
                lastSeen = version;
                Interlocked.Increment(ref newVersions);
            }
        });
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        Volatile.Write(ref newVersions, 0);
        for (var i = 0; i < 50; i++)
        {
            UnifiedTracking.MarkDataUpdated();
        }
        await Task.Delay(100);

        Assert.InRange(Volatile.Read(ref newVersions), 1, 3);
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotStopTheLoop()
    {
        var throwNext = 1;
        var armed = 0;
        var ping = Ping();
        await using var harness = new Harness(_ =>
        {
            if (Interlocked.Exchange(ref throwNext, 0) == 1)
            {
                throw new InvalidOperationException("subscriber failure");
            }
            if (Interlocked.Exchange(ref armed, 0) == 1)
            {
                ParameterSenderService.Enqueue(ping);
            }
        });
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var start = Stopwatch.GetTimestamp();
        Volatile.Write(ref armed, 1);
        UnifiedTracking.MarkDataUpdated();

        Assert.True(harness.WaitForPing(out _, start));
    }

    [Fact]
    public async Task Stop_ReleasesTheTimerResolutionItRaised()
    {
        var raises = 0;
        var releases = 0;
        var hold = new TimerResolutionHold(NullLogger.Instance, () =>
        {
            Interlocked.Increment(ref raises);
            return true;
        }, () => Interlocked.Increment(ref releases));
        var harness = new Harness(_ => { }, hold);
        await harness.Sender.StartAsync(CancellationToken.None);

        UnifiedTracking.MarkDataUpdated();
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref raises));
        Assert.Equal(0, Volatile.Read(ref releases));

        await harness.DisposeAsync();
        Assert.Equal(1, Volatile.Read(ref releases));
    }

    [Fact]
    public async Task StopAsync_ReturnsPromptly()
    {
        var harness = new Harness(_ => { });
        await harness.Sender.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        var start = Stopwatch.GetTimestamp();
        await harness.DisposeAsync();

        Assert.True(Stopwatch.GetElapsedTime(start).TotalMilliseconds < 1000);
    }
}
