using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;
using VRCFaceTracking.Core.Services;
using Xunit.Abstractions;

namespace VRCFaceTracking.PerfTests;

public sealed class MemoryTests(ITestOutputHelper output)
{
    private const double AvatarChangeBudgetMs = 250;
    private const long AvatarCycleHeapGrowthBudgetBytes = 256 * 1024;
    private const long SampleHeapGrowthBudgetBytes = 64 * 1024;

    private static List<Core.Params.Parameter> ApplyAvatar(IParameterDefinition[] definitions)
    {
        var relevant = new List<Core.Params.Parameter>();
        foreach (var parameter in UnifiedTracking.AllParameters)
        {
            relevant.AddRange(parameter.ResetParam(definitions));
        }
        return relevant;
    }

    [Fact]
    public void AvatarChange_StaysWithinCpuBudget()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        var definitions = Tracking.AvatarDefinitions().ToArray();
        ApplyAvatar(definitions);
        ApplyAvatar(Array.Empty<IParameterDefinition>());

        var start = Stopwatch.GetTimestamp();
        var relevant = ApplyAvatar(definitions);
        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        ApplyAvatar(Array.Empty<IParameterDefinition>());
        ParameterSenderService.Clear();

        output.Report("avatar change", $"{ms:N1} ms for {definitions.Length} avatar parameters, {relevant.Count} relevant, budget {AvatarChangeBudgetMs} ms");
        Assert.True(relevant.Count > 200, $"only {relevant.Count} parameters matched the synthetic avatar");
        Assert.True(ms < AvatarChangeBudgetMs, $"{ms:N1} ms per avatar change");
    }

    [Fact]
    public void AvatarChange_Repeated_DoesNotLeakSubscribersOrHeap()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        var definitions = Tracking.AvatarDefinitions().ToArray();
        ApplyAvatar(definitions);
        ApplyAvatar(Array.Empty<IParameterDefinition>());
        ParameterSenderService.Clear();
        var baseSubscribers = Tracking.Subscribers();

        ApplyAvatar(definitions);
        var avatarSubscribers = Tracking.Subscribers();
        ApplyAvatar(Array.Empty<IParameterDefinition>());
        ParameterSenderService.Clear();
        var heapBefore = Measure.LiveHeapBytes();

        for (var i = 0; i < 50; i++)
        {
            ApplyAvatar(definitions);
            Assert.Equal(avatarSubscribers, Tracking.Subscribers());
            ApplyAvatar(Array.Empty<IParameterDefinition>());
            ParameterSenderService.Clear();
        }

        var growth = Measure.LiveHeapBytes() - heapBefore;
        output.Report("avatar cycles", $"{baseSubscribers} -> {avatarSubscribers} subscribers, heap growth {growth / 1024.0:N1} KB over 50 cycles, budget {AvatarCycleHeapGrowthBudgetBytes / 1024} KB");
        Assert.Equal(baseSubscribers, Tracking.Subscribers());
        Assert.True(growth < AvatarCycleHeapGrowthBudgetBytes, $"live heap grew {growth} B over 50 avatar changes");
    }

    [Fact]
    public void IpcSamples_LongRun_AreNotRetained()
    {
        var data = new UnifiedTrackingData();
        var scratch = new byte[ReplyUpdatePacket.PayloadSize];
        ReplyUpdatePacket? latest = null;
        void Sample(int frame)
        {
            data.Shapes[frame % data.Shapes.Length].Weight = frame % 100 / 100f;
            latest = ReplyUpdatePacket.CaptureIfChanged(data, frame + 1, scratch, latest) ?? latest;
            VrcftPacketDecoder.TryDecodePacket(latest!.GetBytes(), out var packet);
            lock (UnifiedTracking.DataLock)
            {
                ((ReplyUpdatePacket)packet).UpdateGlobalState(TrackingCapability.All);
            }
        }

        for (var i = 0; i < 2000; i++)
        {
            Sample(i);
        }
        var heapBefore = Measure.LiveHeapBytes();

        for (var i = 0; i < 50_000; i++)
        {
            Sample(i);
        }

        var growth = Measure.LiveHeapBytes() - heapBefore;
        output.Report("ipc sample heap growth", $"{growth / 1024.0:N1} KB over 50000 samples, budget {SampleHeapGrowthBudgetBytes / 1024} KB");
        Assert.True(growth < SampleHeapGrowthBudgetBytes, $"live heap grew {growth} B over 50000 samples");
    }

    [Fact]
    public void SendQueue_WithoutAnOscTarget_StillDrains()
    {
        var target = new FakeOscTarget();
        var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
        var mutator = Tracking.CreateMutator(allMutations: false);
        using var sender = new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance,
            new TimerResolutionHold(NullLogger.Instance));
        Tracking.SetAllParametersRelevant(true);
        try
        {
            var drained = 0;
            for (var i = 0; i < 100; i++)
            {
                Tracking.WriteFrame(i);
                UnifiedTracking.UpdateData();
                drained += sender.SendQueued();
            }

            Assert.True(drained > 1000, $"only {drained} messages drained");
            Assert.Equal(0, sender.SendQueued());
        }
        finally
        {
            Tracking.SetAllParametersRelevant(false);
        }
    }
}
