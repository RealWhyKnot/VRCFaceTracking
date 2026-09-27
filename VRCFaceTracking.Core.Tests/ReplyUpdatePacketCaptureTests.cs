using System.Runtime.InteropServices;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Tests;

[Collection("UnifiedTrackingState")]
public class ReplyUpdatePacketCaptureTests
{
    private const int ShapeCount = (int)UnifiedExpressions.Max + 1;

    private static UnifiedTrackingData Sample()
    {
        var data = new UnifiedTrackingData();
        for (var i = 0; i < ShapeCount; i++)
        {
            data.Shapes[i].Weight = 0.1f + i * 0.003f;
        }
        data.Eye.Left.Gaze.x = 0.11f;
        data.Eye.Left.Gaze.y = 0.12f;
        data.Eye.Left.PupilDiameter_MM = 3.5f;
        data.Eye.Left.Openness = 0.61f;
        data.Eye.Right.Gaze.x = 0.21f;
        data.Eye.Right.Gaze.y = 0.22f;
        data.Eye.Right.PupilDiameter_MM = 3.6f;
        data.Eye.Right.Openness = 0.62f;
        data.Eye._maxDilation = 5f;
        data.Eye._minDilation = 1f;
        data.Head.HeadYaw = 0.31f;
        data.Head.HeadPitch = 0.32f;
        data.Head.HeadRoll = 0.33f;
        data.Head.HeadPosX = 0.41f;
        data.Head.HeadPosY = 0.42f;
        data.Head.HeadPosZ = 0.43f;
        return data;
    }

    private static byte[] Payload(ReplyUpdatePacket packet) =>
        packet.GetBytes().AsSpan(12, ReplyUpdatePacket.PayloadSize).ToArray();

    [Fact]
    public void PayloadSize_MatchesTheDecodedStruct() =>
        Assert.Equal(ReplyUpdatePacket.PayloadSize, Marshal.SizeOf<ReplyUpdatePacket.UpdateDataContiguous>());

    [Fact]
    public void Capture_RoundTripsEveryMergedField()
    {
        var source = Sample();
        var packet = new ReplyUpdatePacket();
        packet.Decode(ReplyUpdatePacket.Capture(source, 42).GetBytes());

        UnifiedTracking.Data = new UnifiedTrackingData();
        packet.UpdateGlobalState(TrackingCapability.All);
        var merged = UnifiedTracking.Data;

        for (var i = 0; i < (int)UnifiedExpressions.Max; i++)
        {
            Assert.Equal(source.Shapes[i].Weight, merged.Shapes[i].Weight);
        }
        Assert.Equal(source.Eye.Left.Gaze.x, merged.Eye.Left.Gaze.x);
        Assert.Equal(source.Eye.Left.Gaze.y, merged.Eye.Left.Gaze.y);
        Assert.Equal(source.Eye.Left.PupilDiameter_MM, merged.Eye.Left.PupilDiameter_MM);
        Assert.Equal(source.Eye.Left.Openness, merged.Eye.Left.Openness);
        Assert.Equal(source.Eye.Right.Gaze.x, merged.Eye.Right.Gaze.x);
        Assert.Equal(source.Eye.Right.Gaze.y, merged.Eye.Right.Gaze.y);
        Assert.Equal(source.Eye.Right.PupilDiameter_MM, merged.Eye.Right.PupilDiameter_MM);
        Assert.Equal(source.Eye.Right.Openness, merged.Eye.Right.Openness);
        Assert.Equal(source.Eye._maxDilation, merged.Eye._maxDilation);
        Assert.Equal(source.Eye._minDilation, merged.Eye._minDilation);
        Assert.Equal(source.Head.HeadYaw, merged.Head.HeadYaw);
        Assert.Equal(source.Head.HeadPitch, merged.Head.HeadPitch);
        Assert.Equal(source.Head.HeadRoll, merged.Head.HeadRoll);
        Assert.Equal(source.Head.HeadPosX, merged.Head.HeadPosX);
        Assert.Equal(source.Head.HeadPosY, merged.Head.HeadPosY);
        Assert.Equal(source.Head.HeadPosZ, merged.Head.HeadPosZ);
        Assert.Equal(42, packet.SampleTicks);
    }

    [Fact]
    public void CaptureIfChanged_UnchangedData_ReturnsNull()
    {
        var data = Sample();
        var scratch = new byte[ReplyUpdatePacket.PayloadSize];

        var first = ReplyUpdatePacket.CaptureIfChanged(data, 1, scratch, null);

        Assert.NotNull(first);
        Assert.Null(ReplyUpdatePacket.CaptureIfChanged(data, 2, scratch, first));
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("openness")]
    [InlineData("gaze")]
    [InlineData("head")]
    [InlineData("dilation")]
    public void CaptureIfChanged_AnySingleFieldChange_ReturnsPacket(string field)
    {
        var data = Sample();
        var scratch = new byte[ReplyUpdatePacket.PayloadSize];
        var first = ReplyUpdatePacket.CaptureIfChanged(data, 1, scratch, null);

        switch (field)
        {
            case "shape":
                data.Shapes[ShapeCount - 1].Weight += 0.001f;
                break;
            case "openness":
                data.Eye.Right.Openness += 0.001f;
                break;
            case "gaze":
                data.Eye.Left.Gaze.y += 0.001f;
                break;
            case "head":
                data.Head.HeadPosZ += 0.001f;
                break;
            case "dilation":
                data.Eye._minDilation += 0.001f;
                break;
        }

        var second = ReplyUpdatePacket.CaptureIfChanged(data, 2, scratch, first);

        Assert.NotNull(second);
        Assert.Equal(2, second.SampleTicks);
    }

    [Fact]
    public void CaptureIfChanged_NaNStaysUnchanged()
    {
        var data = Sample();
        data.Eye.Left.Openness = float.NaN;
        var scratch = new byte[ReplyUpdatePacket.PayloadSize];

        var first = ReplyUpdatePacket.CaptureIfChanged(data, 1, scratch, null);

        Assert.Null(ReplyUpdatePacket.CaptureIfChanged(data, 2, scratch, first));
    }

    [Fact]
    public void CaptureIfChanged_PacketKeepsItsOwnCopy()
    {
        var data = Sample();
        var scratch = new byte[ReplyUpdatePacket.PayloadSize];
        var first = ReplyUpdatePacket.CaptureIfChanged(data, 1, scratch, null)!;
        var before = Payload(first);

        data.Shapes[0].Weight = 0.99f;
        Assert.NotNull(ReplyUpdatePacket.CaptureIfChanged(data, 2, scratch, first));

        Assert.Equal(before, Payload(first));
    }

    [Fact]
    public void GetBytes_WithoutCapture_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new ReplyUpdatePacket().GetBytes());
}
