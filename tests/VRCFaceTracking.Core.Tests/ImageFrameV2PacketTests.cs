using VRCFaceTracking.Core.Sandboxing;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

[Collection("PartialPacketState")]
public sealed class ImageFrameV2PacketTests
{
    private static byte[] Pattern(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 31) + 7);
        }

        return data;
    }

    [Theory]
    [InlineData(CameraPixelFormat.Rgba8888, 8, 6, 192)]
    [InlineData(CameraPixelFormat.Bgra8888, 5, 3, 60)]
    [InlineData(CameraPixelFormat.Gray8, 16, 16, 256)]
    [InlineData(CameraPixelFormat.Jpeg, 640, 480, 1200)]
    public void FramesRoundTripForEveryFormat(CameraPixelFormat format, int width, int height, int length)
    {
        var source = new ImageFrameV2Packet("left", "Left eye", CameraStreamRole.EyeLeft, format, width, height, CameraControlCapabilities.Recenter | CameraControlCapabilities.Exposure, Pattern(length));
        byte[] bytes = source.GetBytes();

        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? packet));
        ImageFrameV2Packet decoded = Assert.IsType<ImageFrameV2Packet>(packet);
        Assert.Equal("left", decoded.StreamId);
        Assert.Equal("Left eye", decoded.Label);
        Assert.Equal(CameraStreamRole.EyeLeft, decoded.Role);
        Assert.Equal(format, decoded.Format);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(CameraControlCapabilities.Recenter | CameraControlCapabilities.Exposure, decoded.Capabilities);
        Assert.Equal(source.GetData(), decoded.GetData());
    }

    [Fact]
    public void PacketTypesAreAdditive()
    {
        Assert.Equal(1000u, (uint)IpcPacket.PacketType.DebugStreamFrame);
        Assert.Equal(1001u, (uint)IpcPacket.PacketType.DebugStreamFrameV2);
        Assert.Equal(208u, (uint)IpcPacket.PacketType.EventCameraControl);
    }

    [Theory]
    [InlineData(CameraPixelFormat.Rgba8888, 8, 6, 191)]
    [InlineData(CameraPixelFormat.Rgba8888, 8, 6, 193)]
    [InlineData(CameraPixelFormat.Bgra8888, 4, 4, 32)]
    [InlineData(CameraPixelFormat.Gray8, 4, 4, 15)]
    [InlineData(CameraPixelFormat.Gray8, 4, 4, 64)]
    [InlineData(CameraPixelFormat.Jpeg, 4, 4, 0)]
    [InlineData(CameraPixelFormat.Rgba8888, 0, 6, 0)]
    [InlineData(CameraPixelFormat.Gray8, 2049, 1, 2049)]
    [InlineData(CameraPixelFormat.Gray8, 1, 2049, 2049)]
    [InlineData(CameraPixelFormat.Gray8, -1, 4, 4)]
    public void WrongSizesAreRejected(CameraPixelFormat format, int width, int height, int length)
    {
        Assert.False(ImageFrameV2Packet.IsValidFrame(format, width, height, length));

        var source = new ImageFrameV2Packet("a", "A", CameraStreamRole.Other, format, width, height, CameraControlCapabilities.None, new byte[Math.Max(length, 0)]);
        byte[] bytes = source.GetBytes();
        Assert.False(VrcftPacketDecoder.TryDecodePacket(bytes, out _));
        Assert.Throws<InvalidDataException>(() => new ImageFrameV2Packet().Decode(bytes));
    }

    [Fact]
    public void OversizedEncodedFramesAreRejected()
    {
        Assert.True(ImageFrameV2Packet.IsValidFrame(CameraPixelFormat.Jpeg, 100, 100, ImageFrameV2Packet.MaxFrameBytes));
        Assert.False(ImageFrameV2Packet.IsValidFrame(CameraPixelFormat.Jpeg, 100, 100, ImageFrameV2Packet.MaxFrameBytes + 1));
    }

    [Fact]
    public void LargestRawFrameIsAccepted()
    {
        Assert.True(ImageFrameV2Packet.IsValidFrame(CameraPixelFormat.Gray8, ImageFrameV2Packet.MaxDimension, ImageFrameV2Packet.MaxDimension, ImageFrameV2Packet.MaxDimension * ImageFrameV2Packet.MaxDimension));
        Assert.False(ImageFrameV2Packet.IsValidFrame(CameraPixelFormat.Rgba8888, ImageFrameV2Packet.MaxDimension, ImageFrameV2Packet.MaxDimension, ImageFrameV2Packet.MaxDimension * ImageFrameV2Packet.MaxDimension * 4));
    }

    [Fact]
    public void TruncatedBodiesAndHeadersAreRejected()
    {
        var source = new ImageFrameV2Packet("a", "A", CameraStreamRole.Mouth, CameraPixelFormat.Gray8, 4, 4, CameraControlCapabilities.None, Pattern(16));
        byte[] bytes = source.GetBytes();

        Assert.Throws<InvalidDataException>(() => new ImageFrameV2Packet().Decode(bytes[..^1]));
        Assert.Throws<InvalidDataException>(() => new ImageFrameV2Packet().Decode(bytes[..10]));
    }

    [Fact]
    public void UnknownRoleOrFormatBytesAreRejected()
    {
        var source = new ImageFrameV2Packet("a", "A", CameraStreamRole.Mouth, CameraPixelFormat.Gray8, 4, 4, CameraControlCapabilities.None, Pattern(16));

        byte[] role = source.GetBytes();
        role[8] = 200;
        Assert.Throws<InvalidDataException>(() => new ImageFrameV2Packet().Decode(role));

        byte[] format = source.GetBytes();
        format[9] = 200;
        Assert.Throws<InvalidDataException>(() => new ImageFrameV2Packet().Decode(format));
    }

    [Fact]
    public void EmptyStreamIdIsRejected()
    {
        var source = new ImageFrameV2Packet(string.Empty, "A", CameraStreamRole.Mouth, CameraPixelFormat.Gray8, 4, 4, CameraControlCapabilities.None, Pattern(16));
        Assert.Throws<InvalidDataException>(() => new ImageFrameV2Packet().Decode(source.GetBytes()));
    }

    [Fact]
    public void LongTextIsTruncatedToTheWireLimit()
    {
        string longText = new('x', 500);
        var source = new ImageFrameV2Packet(longText, longText, CameraStreamRole.Other, CameraPixelFormat.Gray8, 2, 2, CameraControlCapabilities.None, Pattern(4));

        Assert.True(VrcftPacketDecoder.TryDecodePacket(source.GetBytes(), out IpcPacket? packet));
        ImageFrameV2Packet decoded = Assert.IsType<ImageFrameV2Packet>(packet);
        Assert.Equal(64, decoded.StreamId.Length);
        Assert.Equal(64, decoded.Label.Length);
    }

    [Fact]
    public void LegacyFramePacketStaysIntact()
    {
        byte[] data = Pattern(4 * 4 * 4);
        var legacy = new ImageFrameUpdatePacket(ImageFrameUpdatePacket.EyeKind, 4, 4, data);
        byte[] bytes = legacy.GetBytes();

        Assert.Equal(IpcPacket.PacketType.DebugStreamFrame, legacy.GetPacketType());
        Assert.True(VrcftPacketDecoder.TryDecodePacket(bytes, out IpcPacket? packet));
        ImageFrameUpdatePacket decoded = Assert.IsType<ImageFrameUpdatePacket>(packet);
        Assert.Equal(ImageFrameUpdatePacket.EyeKind, decoded.Kind);
        Assert.Equal(data, decoded.GetData());
    }

    [Fact]
    public void EncodedWrapperKeepsTheOriginalBytes()
    {
        var legacy = new ImageFrameUpdatePacket(ImageFrameUpdatePacket.LipKind, 2, 2, Pattern(16));
        byte[] bytes = legacy.GetBytes();
        var wrapped = new EncodedIpcPacket(IpcPacket.PacketType.DebugStreamFrame, bytes);

        Assert.Same(bytes, wrapped.GetBytes());
        Assert.Equal(IpcPacket.PacketType.DebugStreamFrame, wrapped.GetPacketType());
    }
}
