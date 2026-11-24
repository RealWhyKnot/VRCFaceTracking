using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Tests;

public sealed class CameraDecodeTests
{
    private static readonly byte[] Corrupt = [1, 2, 3, 4, 5];

    private sealed class TrackedBitmap : Bitmap
    {
        public TrackedBitmap(Stream stream)
            : base(stream)
        {
        }

        public bool IsDisposed { get; private set; }

        public override void Dispose()
        {
            IsDisposed = true;
            base.Dispose();
        }
    }

    private sealed class ManualDecoding
    {
        private readonly Queue<Action> _work = new();
        private readonly Queue<Action> _ui = new();

        public ManualDecoding()
        {
            Decoding = new CameraStreamViewModel.Decoding(_work.Enqueue, _ui.Enqueue, Decode);
        }

        public CameraStreamViewModel.Decoding Decoding { get; }

        public List<TrackedBitmap> Made { get; } = [];

        public int Calls { get; private set; }

        public int Workers => _work.Count;

        public int Posts => _ui.Count;

        public void RunWork()
        {
            while (_work.TryDequeue(out Action? work))
            {
                work();
            }
        }

        public void RunUi()
        {
            while (_ui.TryDequeue(out Action? post))
            {
                post();
            }
        }

        public void RunAll()
        {
            RunWork();
            RunUi();
        }

        private TrackedBitmap? Decode(ReadOnlyMemory<byte> data)
        {
            Calls++;
            if (data.Length < 16)
            {
                return null;
            }

            using var stream = new MemoryStream(data.ToArray(), writable: false);
            var bitmap = new TrackedBitmap(stream);
            Made.Add(bitmap);
            return bitmap;
        }
    }

    private static byte[] Encoded(int width, int height)
    {
        using var stream = new MemoryStream();
        using (var source = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul))
        {
            source.Save(stream);
        }

        return stream.ToArray();
    }

    private static byte[] JpegHeaderOnly(byte frameMarker, int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, frameMarker, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        0xFF, 0xD9,
    ];

    private static CameraStreamSnapshot Jpeg(long sequence, byte[] data, int width = 64, int height = 64) =>
        CameraTestData.Snap("cam", CameraPixelFormat.Jpeg, width, height, sequence: sequence, data: data);

    [AvaloniaFact]
    public void EncodedFramesWaitForTheWorkerAndTheUiPost()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);

        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        CameraStreamViewModel stream = cameras.Streams[0];
        Assert.Null(stream.Source);
        Assert.False(stream.HasFrame);
        Assert.Equal(1, decoding.Workers);

        decoding.RunWork();
        Assert.Null(stream.Source);
        Assert.Equal(1, decoding.Posts);

        decoding.RunUi();
        Assert.Same(decoding.Made[0], stream.Source);
        Assert.True(stream.HasFrame);
        Assert.True(stream.ShowCover);
    }

    [AvaloniaFact]
    public void EncodedFrameSizeComesFromTheDecodedImage()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);

        cameras.Refresh([Jpeg(1, Encoded(12, 9), width: 640, height: 640)]);
        decoding.RunAll();
        cameras.Refresh([Jpeg(1, Encoded(12, 9), width: 640, height: 640)]);

        CameraStreamViewModel stream = cameras.Streams[0];
        Assert.Equal(240, stream.FrameWidth);
        Assert.Equal(TrackingText.Format(Resources.CameraDetail_Size, 12, 9), stream.Detail);
    }

    [AvaloniaFact]
    public void OneWorkerDecodesOnlyTheNewestPendingFrame()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        byte[] frame = Encoded(8, 8);

        for (int sequence = 1; sequence <= 5; sequence++)
        {
            cameras.Refresh([Jpeg(sequence, frame)]);
        }

        Assert.Equal(1, decoding.Workers);
        decoding.RunAll();

        Assert.Single(decoding.Made);
        Assert.Same(decoding.Made[0], cameras.Streams[0].Source);
    }

    [AvaloniaFact]
    public void FramesDecodedWhileTheUiIsBusyCollapseIntoOnePost()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        byte[] frame = Encoded(8, 8);

        cameras.Refresh([Jpeg(1, frame)]);
        decoding.RunWork();
        cameras.Refresh([Jpeg(2, frame)]);
        decoding.RunWork();

        Assert.Equal(1, decoding.Posts);
        decoding.RunUi();

        Assert.Equal(2, decoding.Made.Count);
        Assert.True(decoding.Made[0].IsDisposed);
        Assert.Same(decoding.Made[1], cameras.Streams[0].Source);
        Assert.False(decoding.Made[1].IsDisposed);
    }

    [AvaloniaFact]
    public void NewFramesReplaceTheOldBitmapWithOneSourceChange()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        byte[] frame = Encoded(8, 8);
        cameras.Refresh([Jpeg(1, frame)]);
        decoding.RunAll();
        CameraStreamViewModel stream = cameras.Streams[0];
        var seen = new List<Bitmap?>();
        stream.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(CameraStreamViewModel.Source), StringComparison.Ordinal))
            {
                seen.Add(stream.Source);
            }
        };

        cameras.Refresh([Jpeg(2, frame)]);
        decoding.RunAll();

        Assert.Same(decoding.Made[1], Assert.Single(seen));
        Assert.True(decoding.Made[0].IsDisposed);
    }

    [AvaloniaFact]
    public void ResetBeforeDeliveryDisposesTheDecodedFrame()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        CameraStreamViewModel stream = cameras.Streams[0];
        decoding.RunWork();

        cameras.Reset();
        decoding.RunUi();

        Assert.Null(stream.Source);
        Assert.False(stream.HasFrame);
        Assert.All(decoding.Made, bitmap => Assert.True(bitmap.IsDisposed));
    }

    [AvaloniaFact]
    public void ResetDuringDecodeDisposesTheFrameOnTheWorker()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        CameraStreamViewModel stream = cameras.Streams[0];

        cameras.Reset();
        decoding.RunAll();

        Assert.Null(stream.Source);
        Assert.Equal(0, decoding.Posts);
        Assert.All(decoding.Made, bitmap => Assert.True(bitmap.IsDisposed));
    }

    [AvaloniaFact]
    public void RawFramesWinOverAnOlderPendingDecode()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        decoding.RunWork();

        cameras.Refresh([CameraTestData.Snap("cam", CameraPixelFormat.Gray8, 4, 4, sequence: 2)]);
        decoding.RunUi();

        CameraStreamViewModel stream = cameras.Streams[0];
        Assert.IsType<WriteableBitmap>(stream.Source);
        Assert.True(decoding.Made[0].IsDisposed);
        Assert.Equal(TrackingText.Format(Resources.CameraDetail_Size, 4, 4), stream.Detail);
    }

    [AvaloniaFact]
    public void EncodedFramesTakeOverFromRawFrames()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        cameras.Refresh([CameraTestData.Snap("cam", CameraPixelFormat.Gray8, 4, 4, sequence: 1)]);
        Bitmap raw = cameras.Streams[0].Source!;

        cameras.Refresh([Jpeg(2, Encoded(8, 8))]);
        Assert.Same(raw, cameras.Streams[0].Source);
        decoding.RunAll();

        Assert.Same(decoding.Made[0], cameras.Streams[0].Source);
    }

    [AvaloniaFact]
    public void FreezingDropsADecodeThatIsStillInFlight()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        byte[] frame = Encoded(8, 8);
        cameras.Refresh([Jpeg(1, frame)]);
        decoding.RunAll();
        cameras.Refresh([Jpeg(2, frame)]);
        decoding.RunWork();

        cameras.IsFrozen = true;
        cameras.Refresh([Jpeg(2, frame)]);
        decoding.RunUi();
        cameras.Refresh([Jpeg(3, frame)]);

        Assert.Same(decoding.Made[0], cameras.Streams[0].Source);
        Assert.True(decoding.Made[1].IsDisposed);
        Assert.Equal(0, decoding.Workers);
    }

    [AvaloniaFact]
    public void CorruptFramesClearThePictureAndItsBitmap()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        decoding.RunAll();

        cameras.Refresh([Jpeg(2, Corrupt)]);
        decoding.RunAll();
        cameras.Refresh([Jpeg(2, Corrupt)]);

        CameraStreamViewModel stream = cameras.Streams[0];
        Assert.Null(stream.Source);
        Assert.False(stream.HasFrame);
        Assert.False(stream.ShowCover);
        Assert.Equal(Resources.CameraStreamsWaiting, stream.Detail);
        Assert.True(decoding.Made[0].IsDisposed);

        cameras.Refresh([Jpeg(3, Encoded(8, 8))]);
        decoding.RunAll();
        Assert.Same(decoding.Made[1], stream.Source);
        Assert.True(stream.HasFrame);
    }

    [AvaloniaTheory]
    [InlineData(2049, 4)]
    [InlineData(4, 2049)]
    public void OversizedDecodedFramesAreDroppedAndDisposed(int width, int height)
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        decoding.RunAll();

        cameras.Refresh([Jpeg(2, Encoded(width, height))]);
        decoding.RunAll();

        CameraStreamViewModel stream = cameras.Streams[0];
        Assert.Equal(2, decoding.Made.Count);
        Assert.True(decoding.Made[1].IsDisposed);
        Assert.True(decoding.Made[0].IsDisposed);
        Assert.Null(stream.Source);
        Assert.False(stream.HasFrame);
    }

    [AvaloniaFact]
    public void OversizedJpegHeadersAreRejectedBeforeDecoding()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);
        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        decoding.RunAll();

        cameras.Refresh([Jpeg(2, JpegHeaderOnly(0xC0, 65535, 65535))]);
        decoding.RunAll();

        CameraStreamViewModel stream = cameras.Streams[0];
        Assert.Equal(1, decoding.Calls);
        Assert.Single(decoding.Made);
        Assert.True(decoding.Made[0].IsDisposed);
        Assert.Null(stream.Source);
        Assert.False(stream.HasFrame);
        Assert.Equal(0, decoding.Workers);

        cameras.Refresh([Jpeg(3, Encoded(8, 8))]);
        decoding.RunAll();
        Assert.Equal(2, decoding.Calls);
        Assert.Same(decoding.Made[1], stream.Source);
    }

    [Theory]
    [InlineData(0xC0, 65535, 65535, false)]
    [InlineData(0xC2, 2048, 2048, true)]
    [InlineData(0xC1, 2049, 4, false)]
    [InlineData(0xCF, 4, 0, false)]
    [InlineData(0xC4, 65535, 65535, true)]
    public void JpegHeaderReadsTheFrameSize(int marker, int width, int height, bool fits)
    {
        byte[] data = JpegHeaderOnly((byte)marker, width, height);

        Assert.Equal(fits, JpegHeader.Fits(data, ImageFrameV2Packet.MaxDimension));
        Assert.True(JpegHeader.Fits(Corrupt, ImageFrameV2Packet.MaxDimension));
    }

    [AvaloniaFact]
    public void DecodedFramesAtTheSizeLimitAreKept()
    {
        var decoding = new ManualDecoding();
        var cameras = new CameraStreamsViewModel(decoding: decoding.Decoding);

        cameras.Refresh([Jpeg(1, Encoded(ImageFrameV2Packet.MaxDimension, 2))]);
        decoding.RunAll();

        Assert.Same(decoding.Made[0], cameras.Streams[0].Source);
        Assert.False(decoding.Made[0].IsDisposed);
    }

    [AvaloniaFact]
    public void RealDecoderReadsSlicedBuffersWithoutCopying()
    {
        byte[] encoded = Encoded(6, 3);
        byte[] padded = [9, 9, 9, .. encoded, 9, 9];

        using Bitmap? decoded = CameraStreamViewModel.Decoding.DecodeJpeg(padded.AsMemory(3, encoded.Length));

        Assert.NotNull(decoded);
        Assert.Equal(new PixelSize(6, 3), decoded.PixelSize);
        Assert.Null(CameraStreamViewModel.Decoding.DecodeJpeg(Corrupt));
    }

    [AvaloniaFact]
    public async Task DefaultDecoderDeliversOnTheUiThread()
    {
        var cameras = new CameraStreamsViewModel();
        cameras.Refresh([Jpeg(1, Encoded(8, 8))]);
        CameraStreamViewModel stream = cameras.Streams[0];

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (stream.Source == null)
        {
            await Task.Delay(10, timeout.Token);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(new PixelSize(8, 8), stream.Source.PixelSize);
        cameras.Reset();
    }
}
