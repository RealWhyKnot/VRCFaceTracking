using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Perf.Tests;

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
        Tracking.SetAllParametersRelevant(relevant: true);
    }

    public Task StartAsync()
    {
        _started = true;
        return Sender.StartAsync(CancellationToken.None);
    }

    public int Tick()
    {
        TrackingOutput.UpdateData();
        return Sender.SendQueued();
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await Sender.StopAsync(CancellationToken.None);
        }
        Sender.Dispose();
        Tracking.SetAllParametersRelevant(relevant: false);
        Sink.Dispose();
    }
}
