using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace VRCFaceTracking.Core.Tests;

[Collection("PartialPacketState")]
public class ImageFramePacketTests
{
    private static ImageFrameUpdatePacket BuildFrame(byte kind, int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 31);
        }

        return new ImageFrameUpdatePacket { Kind = kind, Width = width, Height = height, Data = data };
    }

    [Theory]
    [InlineData(ImageFrameUpdatePacket.EyeKind, 200, 200)]
    [InlineData(ImageFrameUpdatePacket.LipKind, 128, 96)]
    [InlineData(ImageFrameUpdatePacket.EyeKind, 1, 1)]
    public void ImageFrame_RoundTrips(byte kind, int width, int height)
    {
        var source = BuildFrame(kind, width, height);
        var bytes = source.GetBytes();

        Assert.Equal(ImageFrameUpdatePacket.HeaderSize + width * height * 4, bytes.Length);
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out var packet));
        var decoded = Assert.IsType<ImageFrameUpdatePacket>(packet);
        Assert.Equal(kind, decoded.Kind);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(source.Data, decoded.Data);
    }

    [Fact]
    public void ImageFrame_TruncatedBodyIsRejected()
    {
        var bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes[..^1], out _));
    }

    [Fact]
    public void ImageFrame_MismatchedDataLengthIsRejected()
    {
        var bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        BitConverter.TryWriteBytes(bytes.AsSpan(17), 16 * 16 * 4 + 4);
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ImageFrameUpdatePacket.MaxDimension + 1)]
    [InlineData(int.MaxValue)]
    public void ImageFrame_BadDimensionsAreRejected(int width)
    {
        var bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        BitConverter.TryWriteBytes(bytes.AsSpan(9), width);
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
    }

    [Fact]
    public void ImageFrame_BadKindIsRejected()
    {
        var bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        bytes[8] = 2;
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
    }

    [Fact]
    public void ImageFrame_SurvivesPartialPacketSplit()
    {
        var source = BuildFrame(ImageFrameUpdatePacket.LipKind, 200, 200);
        var bytes = source.GetBytes();
        var chunks = PartialPacket.SplitPacketIntoChunks(bytes, 1500);
        Assert.True(chunks.Length > 1);

        var combined = Array.Empty<byte>();
        foreach (var chunk in chunks)
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }

        Assert.True(VrcftPacketDecoder.TryDecodePacket(combined, out var packet));
        var decoded = Assert.IsType<ImageFrameUpdatePacket>(packet);
        Assert.Equal(source.Data, decoded.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EventSetImageStream_RoundTrips(bool enabled)
    {
        var bytes = new EventSetImageStreamPacket { Enabled = enabled }.GetBytes();
        Assert.Equal(9, bytes.Length);
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out var packet));
        Assert.Equal(enabled, Assert.IsType<EventSetImageStreamPacket>(packet).Enabled);
    }
}
