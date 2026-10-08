using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;
using Xunit.Abstractions;

namespace VRCFaceTracking.PerfTests;

public sealed class IpcTests(ITestOutputHelper output)
{
    private const double DecodeAndMergeBudgetUs = 40;
    private const long DecodeAndMergeBudgetBytes = 256;
    private const long CaptureBudgetBytes = ReplyUpdatePacket.PayloadSize + 128;
    private const long WireBudgetBytes = ReplyUpdatePacket.PayloadSize + 64;

    private static UnifiedTrackingData Sample(int frame)
    {
        var data = new UnifiedTrackingData();
        for (var i = 0; i < data.Shapes.Length; i++)
        {
            data.Shapes[i].Weight = (frame + i) % 100 / 100f;
        }
        data.Eye.Left.Openness = 0.5f;
        data.Eye.Right.Openness = 0.5f;
        return data;
    }

    [Fact]
    public void ModuleCapture_UnchangedSample_AllocatesNothing()
    {
        var data = Sample(1);
        var scratch = new byte[ReplyUpdatePacket.PayloadSize];
        var previous = ReplyUpdatePacket.CaptureIfChanged(data, 1, scratch, null);
        ReplyUpdatePacket? result = null;

        var bytes = Measure.AllocatedBytes(() => result = ReplyUpdatePacket.CaptureIfChanged(data, 2, scratch, previous), 10_000);

        output.Report("capture unchanged", $"{bytes} B over 10000 captures");
        Assert.NotNull(previous);
        Assert.Null(result);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ModuleCapture_ChangedSample_AllocatesOneSnapshot()
    {
        var samples = new[] { Sample(1), Sample(2) };
        var scratch = new byte[ReplyUpdatePacket.PayloadSize];
        ReplyUpdatePacket? previous = null;
        var frame = 0;

        var bytes = Measure.AllocatedBytes(() =>
        {
            previous = ReplyUpdatePacket.CaptureIfChanged(samples[frame++ & 1], frame, scratch, previous);
        }, 1000);

        output.Report("capture changed", $"{bytes / 1000} B per capture, budget {CaptureBudgetBytes} B");
        Assert.True(bytes / 1000 <= CaptureBudgetBytes, $"{bytes / 1000} B per capture");
    }

    [Fact]
    public void ModuleSend_EncodesOneDatagram()
    {
        var packet = ReplyUpdatePacket.Capture(Sample(1), 1);

        var bytes = Measure.AllocatedBytes(() => packet.GetBytes(), 1000);

        output.Report("reply wire encode", $"{bytes / 1000} B per send, budget {WireBudgetBytes} B");
        Assert.True(bytes / 1000 <= WireBudgetBytes, $"{bytes / 1000} B per send");
    }

    [Fact]
    public void HostDecodeAndMerge_StaysWithinCpuAndMemoryBudget()
    {
        var wire = ReplyUpdatePacket.Capture(Sample(3), 42).GetBytes();
        void DecodeAndMerge()
        {
            VrcftPacketDecoder.TryDecodePacket(wire, out var packet);
            lock (UnifiedTracking.DataLock)
            {
                ((ReplyUpdatePacket)packet).UpdateGlobalState(TrackingCapability.All);
            }
        }

        var us = Measure.BestMicrosecondsPerOp(DecodeAndMerge, 2000);
        var bytes = Measure.AllocatedBytes(DecodeAndMerge, 1000);

        output.Report("host decode+merge", $"{us:N2} us and {bytes / 1000} B per sample, budget {DecodeAndMergeBudgetUs} us / {DecodeAndMergeBudgetBytes} B");
        Assert.True(us < DecodeAndMergeBudgetUs, $"{us:N2} us per sample");
        Assert.True(bytes / 1000 <= DecodeAndMergeBudgetBytes, $"{bytes / 1000} B per sample");
        Assert.Equal(Sample(3).Shapes[5].Weight, UnifiedTracking.Data.Shapes[5].Weight);
    }

    [Fact]
    public void PartialPacketReassembly_ScalesLinearlyInChunkCount()
    {
        var payload = new byte[256 * 1024];
        Random.Shared.NextBytes(payload);
        double Reassemble(int chunks)
        {
            var split = PartialPacket.SplitPacketIntoChunks(payload, payload.Length / chunks + 16 + 128);
            return Measure.BestMicrosecondsPerOp(() =>
            {
                var combined = Array.Empty<byte>();
                foreach (var chunk in split)
                {
                    PartialPacket.DecodePacket(chunk, out combined);
                }
                Assert.Equal(payload.Length, combined.Length);
            }, 5, rounds: 7);
        }

        var few = Reassemble(128);
        var many = Reassemble(1024);
        var exponent = Math.Log(many / few) / Math.Log(8);

        output.Report("partial reassembly", $"128 chunks {few:N0} us, 1024 chunks {many:N0} us, exponent {exponent:N2}");
        Assert.True(exponent < 1.5, $"reassembly grows with exponent {exponent:N2} in the chunk count");
    }
}
