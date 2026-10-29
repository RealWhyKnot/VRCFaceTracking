using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Services;
using Xunit.Abstractions;

namespace VRCFaceTracking.Perf.Tests;

public sealed class MemoryTests
{
    private const double AvatarChangeBudgetMs = 250;
    private const long AvatarCycleHeapGrowthBudgetBytes = 256 * 1024;
    private const long SampleHeapGrowthBudgetBytes = 64 * 1024;

    private readonly ITestOutputHelper _output;

    public MemoryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static List<Parameter> ApplyAvatar(IParameterDefinition[] definitions)
    {
        var relevant = new List<Parameter>();
        foreach (Parameter parameter in TrackingOutput.AllParameters)
        {
            relevant.AddRange(parameter.ResetParam(definitions));
        }
        return relevant;
    }

    [Fact]
    public void AvatarChangeStaysWithinCpuBudget()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        IParameterDefinition[] definitions = [.. Tracking.AvatarDefinitions()];
        ApplyAvatar(definitions);
        ApplyAvatar([]);

        long start = Stopwatch.GetTimestamp();
        List<Parameter> relevant = ApplyAvatar(definitions);
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        ApplyAvatar([]);
        ParameterSenderService.Clear();

        _output.Report("avatar change", $"{ms:N1} ms for {definitions.Length} avatar parameters, {relevant.Count} relevant, budget {AvatarChangeBudgetMs} ms");
        Assert.True(relevant.Count > 200, $"only {relevant.Count} parameters matched the synthetic avatar");
        Assert.True(ms < AvatarChangeBudgetMs, $"{ms:N1} ms per avatar change");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CycleAvatar(IParameterDefinition[] definitions)
    {
        ApplyAvatar(definitions);
        int avatarSubscribers = Tracking.Subscribers();
        ApplyAvatar([]);
        ParameterSenderService.Clear();
        return avatarSubscribers;
    }

    [Fact]
    public void AvatarChangeRepeatedDoesNotLeakSubscribersOrHeap()
    {
        ParameterSenderService.AllParametersRelevantStatic = false;
        IParameterDefinition[] definitions = [.. Tracking.AvatarDefinitions()];
        CycleAvatar(definitions);
        int baseSubscribers = Tracking.Subscribers();

        int avatarSubscribers = CycleAvatar(definitions);
        long heapBefore = Measure.LiveHeapBytes();

        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(avatarSubscribers, CycleAvatar(definitions));
        }

        long growth = Measure.LiveHeapBytes() - heapBefore;
        GC.KeepAlive(definitions);
        _output.Report("avatar cycles", $"{baseSubscribers} -> {avatarSubscribers} subscribers, heap growth {growth / 1024.0:N1} KB over 50 cycles, budget {AvatarCycleHeapGrowthBudgetBytes / 1024} KB");
        Assert.Equal(baseSubscribers, Tracking.Subscribers());
        Assert.True(growth < AvatarCycleHeapGrowthBudgetBytes, $"live heap grew {growth} B over 50 avatar changes");
    }

    [Fact]
    public void IpcSamplesLongRunAreNotRetained()
    {
        var data = new UnifiedTrackingData();
        byte[] scratch = new byte[ReplyUpdatePacket.PayloadSize];
        ReplyUpdatePacket? latest = null;
        void Sample(int frame)
        {
            data.Shapes[frame % data.Shapes.Length].Weight = frame % 100 / 100f;
            latest = ReplyUpdatePacket.CaptureIfChanged(data, frame + 1, scratch, latest) ?? latest;
            Assert.True(VrcftPacketDecoder.TryDecodePacket(latest!.GetBytes(), out IpcPacket? packet));
            using (UnifiedTracking.LockData())
            {
                ((ReplyUpdatePacket)packet).UpdateGlobalState(TrackingCapability.All);
            }
        }

        for (int i = 0; i < 2000; i++)
        {
            Sample(i);
        }
        long heapBefore = Measure.LiveHeapBytes();

        for (int i = 0; i < 50_000; i++)
        {
            Sample(i);
        }

        long growth = Measure.LiveHeapBytes() - heapBefore;
        _output.Report("ipc sample heap growth", $"{growth / 1024.0:N1} KB over 50000 samples, budget {SampleHeapGrowthBudgetBytes / 1024} KB");
        Assert.True(growth < SampleHeapGrowthBudgetBytes, $"live heap grew {growth} B over 50000 samples");
    }

    [Fact]
    public void SendQueueWithoutAnOscTargetStillDrains()
    {
        var target = new FakeOscTarget();
        var send = new OscSendService(NullLogger<OscSendService>.Instance, target);
        UnifiedTrackingMutator mutator = Tracking.CreateMutator(allMutations: false);
        using var sender = new ParameterSenderService(send, mutator, NullLogger<ParameterSenderService>.Instance,
            new TimerResolutionHold(NullLogger.Instance));
        Tracking.SetAllParametersRelevant(relevant: true);
        try
        {
            int drained = 0;
            for (int i = 0; i < 100; i++)
            {
                Tracking.WriteFrame(i);
                TrackingOutput.UpdateData();
                drained += sender.SendQueued();
            }

            Assert.True(drained > 1000, $"only {drained} messages drained");
            Assert.Equal(0, sender.SendQueued());
        }
        finally
        {
            Tracking.SetAllParametersRelevant(relevant: false);
        }
    }
}
