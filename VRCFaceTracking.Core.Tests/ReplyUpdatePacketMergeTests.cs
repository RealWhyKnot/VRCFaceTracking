using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Tests;

public class ReplyUpdatePacketMergeTests
{
    private const float Invalid = 0xFFFFFFFF;
    private const int ShapeCount = (int)UnifiedExpressions.Max + 1;

    private static float ShapeValue(int index) => 0.25f + index * 0.001f;

    private static ReplyUpdatePacket CapturePacket(Action<UnifiedTrackingData> fill)
    {
        var data = new UnifiedTrackingData();
        fill(data);
        UnifiedTracking.Data = data;
        byte[] bytes;
        try
        {
            bytes = new ReplyUpdatePacket().GetBytes();
        }
        finally
        {
            UnifiedTracking.Data = new UnifiedTrackingData();
        }

        var packet = new ReplyUpdatePacket();
        packet.Decode(bytes);
        return packet;
    }

    private static void FillEverything(UnifiedTrackingData data)
    {
        for (var i = 0; i < ShapeCount; i++)
        {
            data.Shapes[i].Weight = ShapeValue(i);
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
    }

    private static IEnumerable<int> IndicesFor(TrackingCapability capability)
    {
        foreach (var (rangeCapability, start, endInclusive) in TrackingCapabilities.ShapeRanges)
        {
            if (!capability.HasFlag(rangeCapability))
            {
                continue;
            }
            for (var i = start; i <= endInclusive; i++)
            {
                yield return i;
            }
        }
    }

    [Fact]
    public void ShapeRanges_AreDisjointAndCoverEveryShape()
    {
        var covered = new HashSet<int>();
        foreach (var (_, start, endInclusive) in TrackingCapabilities.ShapeRanges)
        {
            Assert.True(start <= endInclusive);
            for (var i = start; i <= endInclusive; i++)
            {
                Assert.True(covered.Add(i));
            }
        }

        Assert.Equal(Enumerable.Range(0, (int)UnifiedExpressions.Max), covered.Order());
    }

    [Fact]
    public void ShapeRanges_MatchExpectedBoundaries()
    {
        Assert.Contains((TrackingCapability.Eyes, (int)UnifiedExpressions.EyeSquintRight, (int)UnifiedExpressions.EyeWideLeft), TrackingCapabilities.ShapeRanges);
        Assert.Contains((TrackingCapability.Brows, (int)UnifiedExpressions.BrowPinchRight, (int)UnifiedExpressions.BrowOuterUpLeft), TrackingCapabilities.ShapeRanges);
        Assert.Contains((TrackingCapability.Mouth, (int)UnifiedExpressions.NasalDilationRight, (int)UnifiedExpressions.MouthTightenerLeft), TrackingCapabilities.ShapeRanges);
        Assert.Contains((TrackingCapability.Tongue, (int)UnifiedExpressions.TongueOut, (int)UnifiedExpressions.TongueTwistLeft), TrackingCapabilities.ShapeRanges);
        Assert.Contains((TrackingCapability.Mouth, (int)UnifiedExpressions.SoftPalateClose, (int)UnifiedExpressions.NeckFlexLeft), TrackingCapabilities.ShapeRanges);
    }

    [Theory]
    [InlineData(TrackingCapability.Eyes)]
    [InlineData(TrackingCapability.Brows)]
    [InlineData(TrackingCapability.Mouth)]
    [InlineData(TrackingCapability.Tongue)]
    [InlineData(TrackingCapability.Eyes | TrackingCapability.Brows)]
    [InlineData(TrackingCapability.Mouth | TrackingCapability.Tongue)]
    public void UpdateGlobalState_WritesOnlyAllowedShapeRanges(TrackingCapability allowed)
    {
        var packet = CapturePacket(FillEverything);

        packet.UpdateGlobalState(allowed);

        var expected = IndicesFor(allowed).ToHashSet();
        for (var i = 0; i < (int)UnifiedExpressions.Max; i++)
        {
            if (expected.Contains(i))
            {
                Assert.Equal(ShapeValue(i), UnifiedTracking.Data.Shapes[i].Weight);
            }
            else
            {
                Assert.Equal(0f, UnifiedTracking.Data.Shapes[i].Weight);
            }
        }
    }

    [Fact]
    public void UpdateGlobalState_None_WritesNothing()
    {
        var packet = CapturePacket(FillEverything);

        packet.UpdateGlobalState(TrackingCapability.None);

        for (var i = 0; i < ShapeCount; i++)
        {
            Assert.Equal(0f, UnifiedTracking.Data.Shapes[i].Weight);
        }
        Assert.Equal(1f, UnifiedTracking.Data.Eye.Left.Openness);
        Assert.Equal(0f, UnifiedTracking.Data.Head.HeadYaw);
    }

    [Fact]
    public void UpdateGlobalState_Eyes_WritesEyeStructWithoutHeadOrMouth()
    {
        var packet = CapturePacket(FillEverything);

        packet.UpdateGlobalState(TrackingCapability.Eyes);

        Assert.Equal(0.11f, UnifiedTracking.Data.Eye.Left.Gaze.x);
        Assert.Equal(0.62f, UnifiedTracking.Data.Eye.Right.Openness);
        Assert.Equal(5f, UnifiedTracking.Data.Eye._maxDilation);
        Assert.Equal(0f, UnifiedTracking.Data.Head.HeadYaw);
        Assert.Equal(0f, UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight);
    }

    [Fact]
    public void UpdateGlobalState_Head_WritesHeadOnly()
    {
        var packet = CapturePacket(FillEverything);

        packet.UpdateGlobalState(TrackingCapability.Head);

        Assert.Equal(0.31f, UnifiedTracking.Data.Head.HeadYaw);
        Assert.Equal(0.43f, UnifiedTracking.Data.Head.HeadPosZ);
        Assert.Equal(1f, UnifiedTracking.Data.Eye.Left.Openness);
        for (var i = 0; i < ShapeCount; i++)
        {
            Assert.Equal(0f, UnifiedTracking.Data.Shapes[i].Weight);
        }
    }

    [Fact]
    public void UpdateGlobalState_BrowOuterUpLeft_IsWrittenOnlyByBrows()
    {
        var index = (int)UnifiedExpressions.BrowOuterUpLeft;

        var packet = CapturePacket(FillEverything);
        packet.UpdateGlobalState(TrackingCapability.Mouth | TrackingCapability.Tongue | TrackingCapability.Eyes);
        Assert.Equal(0f, UnifiedTracking.Data.Shapes[index].Weight);

        packet.UpdateGlobalState(TrackingCapability.Brows);
        Assert.Equal(ShapeValue(index), UnifiedTracking.Data.Shapes[index].Weight);
    }

    [Fact]
    public void UpdateGlobalState_SkipsSentinelFields()
    {
        var jawOpen = (int)UnifiedExpressions.JawOpen;
        var packet = CapturePacket(data =>
        {
            FillEverything(data);
            data.Shapes[jawOpen].Weight = Invalid;
            data.Eye.Left.Openness = Invalid;
            data.Head.HeadYaw = Invalid;
        });

        UnifiedTracking.Data.Shapes[jawOpen].Weight = 0.9f;
        UnifiedTracking.Data.Eye.Left.Openness = 0.8f;
        UnifiedTracking.Data.Head.HeadYaw = 0.7f;

        packet.UpdateGlobalState(TrackingCapability.All);

        Assert.Equal(0.9f, UnifiedTracking.Data.Shapes[jawOpen].Weight);
        Assert.Equal(0.8f, UnifiedTracking.Data.Eye.Left.Openness);
        Assert.Equal(0.7f, UnifiedTracking.Data.Head.HeadYaw);
        Assert.Equal(ShapeValue(jawOpen + 1), UnifiedTracking.Data.Shapes[jawOpen + 1].Weight);
    }
}
