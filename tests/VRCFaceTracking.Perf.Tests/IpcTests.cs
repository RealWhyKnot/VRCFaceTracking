using System.Security.Cryptography;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using Xunit.Abstractions;

namespace VRCFaceTracking.Perf.Tests;

public sealed class IpcTests
{
    private const double DecodeAndMergeBudgetUs = 40;
    private const long DecodeAndMergeBudgetBytes = 256;
    private const long CaptureBudgetBytes = ReplyUpdatePacket.PayloadSize + 128;
    private const long WireBudgetBytes = ReplyUpdatePacket.PayloadSize + 64;

    private readonly ITestOutputHelper _output;

    public IpcTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static UnifiedTrackingData Sample(int frame)
    {
        var data = new UnifiedTrackingData();
        for (int i = 0; i < data.Shapes.Length; i++)
        {
            data.Shapes[i].Weight = (frame + i) % 100 / 100f;
        }
        data.Eye.Left.Openness = 0.5f;
        data.Eye.Right.Openness = 0.5f;
        return data;
    }

    [Fact]
    public void ModuleCaptureUnchangedSampleAllocatesNothing()
    {
        UnifiedTrackingData data = Sample(1);
        byte[] scratch = new byte[ReplyUpdatePacket.PayloadSize];
        var previous = ReplyUpdatePacket.CaptureIfChanged(data, 1, scratch, previous: null);
        ReplyUpdatePacket? result = null;

        long bytes = Measure.AllocatedBytes(() => result = ReplyUpdatePacket.CaptureIfChanged(data, 2, scratch, previous), 10_000);

        _output.Report("capture unchanged", $"{bytes} B over 10000 captures");
        Assert.NotNull(previous);
        Assert.Null(result);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ModuleCaptureChangedSampleAllocatesOneSnapshot()
    {
        UnifiedTrackingData[] samples = [Sample(1), Sample(2)];
        byte[] scratch = new byte[ReplyUpdatePacket.PayloadSize];
        ReplyUpdatePacket? previous = null;
        int frame = 0;

        long bytes = Measure.AllocatedBytes(() =>
        {
            previous = ReplyUpdatePacket.CaptureIfChanged(samples[frame++ & 1], frame, scratch, previous);
        }, 1000);

        _output.Report("capture changed", $"{bytes / 1000} B per capture, budget {CaptureBudgetBytes} B");
        Assert.True(bytes / 1000 <= CaptureBudgetBytes, $"{bytes / 1000} B per capture");
    }

    [Fact]
    public void ModuleSendEncodesOneDatagram()
    {
        var packet = ReplyUpdatePacket.Capture(Sample(1), 1);

        long bytes = Measure.AllocatedBytes(() => packet.GetBytes(), 1000);

        _output.Report("reply wire encode", $"{bytes / 1000} B per send, budget {WireBudgetBytes} B");
        Assert.True(bytes / 1000 <= WireBudgetBytes, $"{bytes / 1000} B per send");
    }

    [Fact]
    public void HostDecodeAndMergeStaysWithinCpuAndMemoryBudget()
    {
        byte[] wire = ReplyUpdatePacket.Capture(Sample(3), 42).GetBytes();
        void DecodeAndMerge()
        {
            Assert.True(VrcftPacketDecoder.TryDecodePacket(wire, out IpcPacket? packet));
            using (UnifiedTracking.LockData())
            {
                ((ReplyUpdatePacket)packet).UpdateGlobalState(TrackingCapability.All);
            }
        }

        double us = Measure.BestMicrosecondsPerOp(DecodeAndMerge, 2000);
        long bytes = Measure.AllocatedBytes(DecodeAndMerge, 1000);

        _output.Report("host decode+merge", $"{us:N2} us and {bytes / 1000} B per sample, budget {DecodeAndMergeBudgetUs} us / {DecodeAndMergeBudgetBytes} B");
        Assert.True(us < DecodeAndMergeBudgetUs, $"{us:N2} us per sample");
        Assert.True(bytes / 1000 <= DecodeAndMergeBudgetBytes, $"{bytes / 1000} B per sample");
        Assert.Equal(Sample(3).Shapes[5].Weight, UnifiedTracking.Data.Shapes[5].Weight);
    }

    [Fact]
    public void PartialPacketReassemblyScalesLinearlyInChunkCount()
    {
        byte[] payload = new byte[256 * 1024];
        RandomNumberGenerator.Fill(payload);
        double Reassemble(int chunks)
        {
            byte[][] split = PartialPacket.SplitPacketIntoChunks(payload, (payload.Length / chunks) + 16 + 128);
            return Measure.BestMicrosecondsPerOp(() =>
            {
                byte[]? combined = [];
                foreach (byte[] chunk in split)
                {
                    PartialPacket.DecodePacket(chunk, out combined);
                }
                Assert.Equal(payload.Length, combined.Length);
            }, 5, rounds: 7);
        }

        double few = Reassemble(128);
        double many = Reassemble(1024);
        double exponent = Math.Log(many / few) / Math.Log(8);

        _output.Report("partial reassembly", $"128 chunks {few:N0} us, 1024 chunks {many:N0} us, exponent {exponent:N2}");
        Assert.True(exponent < 1.5, $"reassembly grows with exponent {exponent:N2} in the chunk count");
    }
}
