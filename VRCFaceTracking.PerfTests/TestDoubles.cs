using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.PerfTests;

internal sealed class NullSettings : ILocalSettingsService
{
    public Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) => Task.FromResult(defaultValue!);
    public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false) => Task.CompletedTask;
    public Task Save(object target) => Task.CompletedTask;
    public Task Load(object target) => Task.CompletedTask;
    public Task FlushAsync() => Task.CompletedTask;
}

internal sealed class InlineDispatcher : IDispatcherService
{
    public void Run(Action action) => action();
}

internal sealed class FakeOscTarget : IOscTarget
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

internal sealed record ParameterDefinition(string Address, string Name, Type Type) : IParameterDefinition;

internal static class Tracking
{
    public static void MatchHostProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            Core.Utils.OptOutOfPowerThrottling(out _, out _);
        }
    }

    public static UnifiedTrackingMutator CreateMutator(bool allMutations)
    {
        var mutator = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, new NullSettings(), new InlineDispatcher());
        if (allMutations)
        {
            foreach (var mutation in TrackingMutation.GetImplementingMutations())
            {
                mutation.Logger = NullLogger.Instance;
                mutation.IsActive = true;
                mutator._mutations.Add(mutation);
            }
            mutator.Enabled = true;
            mutator.Initialize();
        }
        return mutator;
    }

    public static void SetAllParametersRelevant(bool relevant)
    {
        ParameterSenderService.AllParametersRelevantStatic = relevant;
        foreach (var parameter in UnifiedTracking.AllParameters)
        {
            parameter.ResetParam(Array.Empty<IParameterDefinition>());
        }
        ParameterSenderService.Clear();
    }

    public static int Subscribers()
    {
        var field = typeof(UnifiedTracking).GetField(nameof(UnifiedTracking.OnUnifiedDataUpdated), BindingFlags.NonPublic | BindingFlags.Static)!;
        return (field.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    public static void WriteFrame(int frame)
    {
        var weight = (frame % 100) / 100f;
        lock (UnifiedTracking.DataLock)
        {
            var data = UnifiedTracking.Data;
            for (var i = 0; i < data.Shapes.Length; i++)
            {
                data.Shapes[i].Weight = weight;
            }
            data.Eye.Left.Openness = 1 - weight;
            data.Eye.Right.Openness = 1 - weight;
            data.Eye.Left.Gaze.x = weight - 0.5f;
            data.Eye.Right.Gaze.x = weight - 0.5f;
            data.Eye.Left.PupilDiameter_MM = 3 + weight;
            data.Eye.Right.PupilDiameter_MM = 3 + weight;
            data.Head.HeadYaw = weight - 0.5f;
        }
    }

    public static List<IParameterDefinition> AvatarDefinitions()
    {
        var definitions = new List<IParameterDefinition>();
        var seen = new HashSet<string>();
        foreach (var parameter in UnifiedTracking.AllParameters)
        {
            foreach (var (name, _) in parameter.GetParamNames())
            {
                if (!name.StartsWith("v2/", StringComparison.Ordinal) || !seen.Add(name))
                {
                    continue;
                }
                definitions.Add(new ParameterDefinition($"/avatar/parameters/FT/{name}", $"FT/{name}", typeof(float)));
                definitions.Add(new ParameterDefinition($"/avatar/parameters/FT/{name}Negative", $"FT/{name}Negative", typeof(bool)));
                foreach (var step in new[] { 1, 2, 4 })
                {
                    definitions.Add(new ParameterDefinition($"/avatar/parameters/FT/{name}{step}", $"FT/{name}{step}", typeof(bool)));
                }
            }
        }
        return definitions;
    }
}

internal sealed class OscSink : IDisposable
{
    public readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly Thread? _drain;
    private long _datagrams;

    public OscSink(bool drain)
    {
        Socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Socket.ReceiveBufferSize = 4 * 1024 * 1024;
        if (!drain)
        {
            return;
        }

        Socket.ReceiveTimeout = 250;
        _drain = new Thread(() =>
        {
            var buffer = new byte[65536];
            while (true)
            {
                try
                {
                    Socket.Receive(buffer);
                    Interlocked.Increment(ref _datagrams);
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "OSC Sink",
        };
        _drain.Start();
    }

    public int Port => ((IPEndPoint)Socket.LocalEndPoint!).Port;

    public long Datagrams => Interlocked.Read(ref _datagrams);

    public void Dispose()
    {
        Socket.Dispose();
        _drain?.Join(1000);
    }
}

internal sealed class SenderHarness : IAsyncDisposable
{
    public readonly OscSink Sink;
    public readonly ParameterSenderService Sender;
    public readonly UnifiedTrackingMutator Mutator;
    private bool _started;

    public SenderHarness(bool allMutations, bool drainSink = false, TimerResolutionHold? timerResolution = null)
    {
        Tracking.MatchHostProcess();
        Sink = new OscSink(drainSink);
        var target = new FakeOscTarget();
        var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
        target.OutPort = Sink.Port;
        Mutator = Tracking.CreateMutator(allMutations);
        Sender = new ParameterSenderService(send, Mutator, NullLogger<ParameterSenderService>.Instance,
            timerResolution ?? new TimerResolutionHold(NullLogger.Instance));
        Tracking.SetAllParametersRelevant(true);
    }

    public Task StartAsync()
    {
        _started = true;
        return Sender.StartAsync(CancellationToken.None);
    }

    public int Tick()
    {
        UnifiedTracking.UpdateData();
        return Sender.SendQueued();
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await Sender.StopAsync(CancellationToken.None);
        }
        Sender.Dispose();
        Tracking.SetAllParametersRelevant(false);
        Sink.Dispose();
    }
}
