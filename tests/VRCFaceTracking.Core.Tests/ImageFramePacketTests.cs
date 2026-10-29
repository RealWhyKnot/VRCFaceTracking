using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;

namespace VRCFaceTracking.Core.Tests;

[Collection("PartialPacketState")]
public sealed class ImageFramePacketTests
{
    private static ImageFrameUpdatePacket BuildFrame(byte kind, int width, int height)
    {
        byte[] data = new byte[width * height * 4];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 31);
        }

        return new ImageFrameUpdatePacket(kind, width, height, data);
    }

    [Theory]
    [InlineData(ImageFrameUpdatePacket.EyeKind, 200, 200)]
    [InlineData(ImageFrameUpdatePacket.LipKind, 128, 96)]
    [InlineData(ImageFrameUpdatePacket.EyeKind, 1, 1)]
    public void ImageFrameRoundTrips(byte kind, int width, int height)
    {
        ImageFrameUpdatePacket source = BuildFrame(kind, width, height);
        byte[] bytes = source.GetBytes();

        Assert.Equal(ImageFrameUpdatePacket.HeaderSize + (width * height * 4), bytes.Length);
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? packet));
        ImageFrameUpdatePacket decoded = Assert.IsType<ImageFrameUpdatePacket>(packet);
        Assert.Equal(kind, decoded.Kind);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(source.GetData(), decoded.GetData());
    }

    [Fact]
    public void ImageFrameTruncatedBodyIsRejected()
    {
        byte[] bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes[..^1], out _));
    }

    [Fact]
    public void ImageFrameMismatchedDataLengthIsRejected()
    {
        byte[] bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        BitConverter.TryWriteBytes(bytes.AsSpan(17), (16 * 16 * 4) + 4);
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ImageFrameUpdatePacket.MaxDimension + 1)]
    [InlineData(int.MaxValue)]
    public void ImageFrameBadDimensionsAreRejected(int width)
    {
        byte[] bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        BitConverter.TryWriteBytes(bytes.AsSpan(9), width);
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
    }

    [Fact]
    public void ImageFrameBadKindIsRejected()
    {
        byte[] bytes = BuildFrame(ImageFrameUpdatePacket.EyeKind, 16, 16).GetBytes();
        bytes[8] = 2;
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
    }

    [Fact]
    public void ImageFrameSurvivesPartialPacketSplit()
    {
        ImageFrameUpdatePacket source = BuildFrame(ImageFrameUpdatePacket.LipKind, 200, 200);
        byte[] bytes = source.GetBytes();
        byte[][] chunks = PartialPacket.SplitPacketIntoChunks(bytes, 1500);
        Assert.True(chunks.Length > 1);

        byte[]? combined = [];
        foreach (byte[] chunk in chunks)
        {
            PartialPacket.DecodePacket(chunk, out combined);
        }

        Assert.True(VrcftPacketDecoder.TryDecodePacket(combined, out IpcPacket? packet));
        ImageFrameUpdatePacket decoded = Assert.IsType<ImageFrameUpdatePacket>(packet);
        Assert.Equal(source.GetData(), decoded.GetData());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EventSetImageStreamRoundTrips(bool enabled)
    {
        byte[] bytes = new EventSetImageStreamPacket { Enabled = enabled }.GetBytes();
        Assert.Equal(9, bytes.Length);
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? packet));
        Assert.Equal(enabled, Assert.IsType<EventSetImageStreamPacket>(packet).Enabled);
    }
}
