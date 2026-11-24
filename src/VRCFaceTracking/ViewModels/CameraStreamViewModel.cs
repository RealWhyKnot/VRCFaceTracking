using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Sandboxing.Ipc;
using VRCFaceTracking.Core.Types;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class CameraStreamViewModel : ObservableObject
{
    private const double FrameHeight = 180;
    private const double MinFrameWidth = 120;
    private const double MaxFrameWidth = 320;
    private const double DefaultFrameWidth = 240;

    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(1);

    private static readonly PropertyChangedEventArgs SourceArgs = new(nameof(Source));
    private static readonly PropertyChangedEventArgs HasFrameArgs = new(nameof(HasFrame));
    private static readonly PropertyChangedEventArgs ShowCoverArgs = new(nameof(ShowCover));
    private static readonly PropertyChangedEventArgs DetailArgs = new(nameof(Detail));

    private readonly TimeProvider _time;
    private readonly Func<Guid, CameraControlCommand, bool> _send;
    private readonly Decoding _decoding;
    private readonly Lock _gate = new();
    private readonly Action _decodeLoop;
    private readonly Action _deliver;
    private byte[] _scratch = [];
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private Bitmap? _decoded;
    private CameraStreamSnapshot? _pending;
    private Bitmap? _ready;
    private bool _hasReady;
    private bool _decodeRunning;
    private bool _deliveryPosted;
    private int _generation;
    private int _pendingGeneration;
    private int _readyGeneration;
    private long _lastFrameStamp;
    private long _windowStamp;
    private int _windowCount;
    private int _rate;
    private int _width;
    private int _height;
    private bool _frozen;
    private (int Width, int Height, int Rate)? _shown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCover))]
    private bool _isBlurred = true;

    [ObservableProperty] private bool _showGuides;

    [ObservableProperty] private string _title;

    [ObservableProperty] private double _frameWidth = DefaultFrameWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecenter))]
    [NotifyPropertyChangedFor(nameof(CanRecalibrate))]
    [NotifyPropertyChangedFor(nameof(HasControls))]
    private CameraControlCapabilities _capabilities;

    public CameraStreamViewModel(CameraStreamSnapshot first, Func<Guid, CameraControlCommand, bool> send, TimeProvider time, Decoding? decoding = null)
    {
        ModuleId = first.ModuleId;
        StreamId = first.StreamId;
        _send = send;
        _time = time;
        _decoding = decoding ?? Decoding.Default;
        _decodeLoop = DecodeLoop;
        _deliver = Deliver;
        _title = TitleFor(first);
        Detail = Resources.CameraStreamsWaiting;
    }

    public Guid ModuleId { get; }

    public long LastSequence { get; private set; } = -1;

    public string StreamId { get; }

    public Bitmap? Source { get; private set; }

    public bool HasFrame { get; private set; }

    public bool ShowCover => IsBlurred && HasFrame;

    public string Detail { get; private set; }

    public bool CanRecenter => Capabilities.HasFlag(CameraControlCapabilities.Recenter);

    public bool HasControls => CanRecenter || CanRecalibrate;

    public bool CanRecalibrate => Capabilities.HasFlag(CameraControlCapabilities.Recalibrate);

    public static string KeyOf(Guid moduleId, string streamId) => string.Concat(moduleId.ToString("N"), "/", streamId);

    public bool Matches(CameraStreamSnapshot snapshot) => snapshot.ModuleId == ModuleId && string.Equals(snapshot.StreamId, StreamId, StringComparison.Ordinal);

    public bool Update(CameraStreamSnapshot snapshot, bool frozen)
    {
        long now = _time.GetTimestamp();
        _frozen = frozen;
        Title = TitleFor(snapshot);
        Capabilities = snapshot.Capabilities;
        if (snapshot.Sequence != LastSequence && CameraPixels.IsDrawable(snapshot.Format, snapshot.Width, snapshot.Height, snapshot.Data.Length))
        {
            AcceptFrame(snapshot, frozen, now);
        }

        return Tick(frozen, now);
    }

    public bool Tick(bool frozen) => Tick(frozen, _time.GetTimestamp());

    public void Clear()
    {
        Release();
        _lastFrameStamp = 0;
        _windowStamp = 0;
        _windowCount = 0;
        _rate = 0;
        _shown = null;
        SetText(Resources.CameraStreamsWaiting);
    }

    private static string TitleFor(CameraStreamSnapshot snapshot)
    {
        string legacy = string.Equals(snapshot.StreamId, CameraStreamRegistry.LegacyEyeId, StringComparison.Ordinal)
            ? Resources.CameraStreamsEye
            : Resources.CameraStreamsLip;
        return snapshot.IsLegacy ? legacy : snapshot.Label.Length > 0 ? snapshot.Label : snapshot.StreamId;
    }

    private static double WidthFor(int width, int height) => Math.Clamp(Math.Round(FrameHeight * width / height), MinFrameWidth, MaxFrameWidth);

    private bool Tick(bool frozen, long now)
    {
        _frozen = frozen;
        if (_lastFrameStamp == 0 || _time.GetElapsedTime(_lastFrameStamp, now) >= StaleAfter)
        {
            Clear();
            return false;
        }

        TimeSpan elapsed = _time.GetElapsedTime(_windowStamp, now);
        if (elapsed >= RateWindow)
        {
            _rate = (int)Math.Round(_windowCount / elapsed.TotalSeconds);
            _windowCount = 0;
            _windowStamp = now;
        }

        SetDetail(frozen);
        return true;
    }

    private void AcceptFrame(CameraStreamSnapshot snapshot, bool frozen, long now)
    {
        if (_lastFrameStamp == 0 || _windowStamp == 0)
        {
            _windowStamp = now;
            _windowCount = 0;
        }

        LastSequence = snapshot.Sequence;
        _lastFrameStamp = now;
        _windowCount++;
        if (frozen && Source != null)
        {
            return;
        }

        if (snapshot.Format == CameraPixelFormat.Jpeg)
        {
            QueueDecode(snapshot);
            return;
        }

        _width = snapshot.Width;
        _height = snapshot.Height;
        FrameWidth = WidthFor(snapshot.Width, snapshot.Height);
        DrawRaw(snapshot);
    }

    private void SetDetail(bool frozen)
    {
        if (!HasFrame)
        {
            _shown = null;
            SetText(Resources.CameraStreamsWaiting);
            return;
        }

        (int Width, int Height, int Rate) shown = (_width, _height, frozen ? -1 : _rate);
        if (_shown == shown)
        {
            return;
        }

        _shown = shown;
        SetText(frozen
            ? TrackingText.Format(Resources.CameraDetail_Frozen, _width, _height)
            : _rate > 0
                ? TrackingText.Format(Resources.CameraDetail_Rate, _width, _height, _rate.ToString(CultureInfo.CurrentCulture))
                : TrackingText.Format(Resources.CameraDetail_Size, _width, _height));
    }

    private void SetText(string text)
    {
        if (string.Equals(text, Detail, StringComparison.Ordinal))
        {
            return;
        }

        Detail = text;
        OnPropertyChanged(DetailArgs);
    }

    private void DrawRaw(CameraStreamSnapshot snapshot)
    {
        Invalidate();
        int width = snapshot.Width;
        int height = snapshot.Height;
        var size = new PixelSize(width, height);
        if (_back != null && _back.PixelSize != size)
        {
            _back.Dispose();
            _back = null;
        }

        int bytes = width * height * CameraPixels.BytesPerPixel;
        if (_scratch.Length < bytes)
        {
            _scratch = new byte[bytes];
        }

        CameraPixels.ToBgra(snapshot.Format, snapshot.Data.Span, width * height, _scratch);
        WriteableBitmap target = _back ?? new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer buffer = target.Lock())
        {
            int rowBytes = width * CameraPixels.BytesPerPixel;
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(_scratch, row * rowBytes, buffer.Address + (row * buffer.RowBytes), rowBytes);
            }
        }

        WriteableBitmap? previous = _front;
        Bitmap? decoded = _decoded;
        _front = target;
        _back = previous != null && previous.PixelSize == size ? previous : null;
        _decoded = null;
        SetSource(target);
        if (previous != null && _back == null)
        {
            previous.Dispose();
        }

        decoded?.Dispose();
    }

    private void QueueDecode(CameraStreamSnapshot snapshot)
    {
        lock (_gate)
        {
            _pending = snapshot;
            _pendingGeneration = _generation;
            if (_decodeRunning)
            {
                return;
            }

            _decodeRunning = true;
        }

        _decoding.Run(_decodeLoop);
    }

    private void DecodeLoop()
    {
        bool finished = false;
        try
        {
            DecodePending();
            finished = true;
        }
        finally
        {
            if (!finished)
            {
                RecoverDecode();
            }
        }
    }

    private void RecoverDecode()
    {
        lock (_gate)
        {
            if (_pending == null)
            {
                _decodeRunning = false;
                return;
            }
        }

        _decoding.Run(_decodeLoop);
    }

    private void DecodePending()
    {
        while (true)
        {
            CameraStreamSnapshot snapshot;
            int generation;
            lock (_gate)
            {
                if (_pending == null)
                {
                    _decodeRunning = false;
                    return;
                }

                snapshot = _pending;
                generation = _pendingGeneration;
                _pending = null;
            }

            Bitmap? decoded = JpegHeader.Fits(snapshot.Data.Span, ImageFrameV2Packet.MaxDimension)
                ? Checked(_decoding.Decode(snapshot.Data))
                : null;
            Bitmap? dropped;
            bool post = false;
            lock (_gate)
            {
                if (generation != _generation)
                {
                    dropped = decoded;
                }
                else
                {
                    dropped = _ready;
                    _ready = decoded;
                    _hasReady = true;
                    _readyGeneration = generation;
                    post = !_deliveryPosted;
                    _deliveryPosted = true;
                }
            }

            dropped?.Dispose();
            if (post)
            {
                _decoding.Post(_deliver);
            }
        }
    }

    private static Bitmap? Checked(Bitmap? decoded)
    {
        if (decoded == null)
        {
            return null;
        }

        PixelSize size = decoded.PixelSize;
        if (size.Width >= 1 && size.Width <= ImageFrameV2Packet.MaxDimension && size.Height >= 1 && size.Height <= ImageFrameV2Packet.MaxDimension)
        {
            return decoded;
        }

        decoded.Dispose();
        return null;
    }

    private void Deliver()
    {
        Bitmap? decoded;
        bool current;
        lock (_gate)
        {
            _deliveryPosted = false;
            if (!_hasReady)
            {
                return;
            }

            decoded = _ready;
            current = _readyGeneration == _generation;
            _ready = null;
            _hasReady = false;
        }

        if (!current || (_frozen && Source != null))
        {
            decoded?.Dispose();
            return;
        }

        if (decoded == null)
        {
            DropSource();
            SetDetail(_frozen);
            return;
        }

        PixelSize size = decoded.PixelSize;
        _width = size.Width;
        _height = size.Height;
        FrameWidth = WidthFor(size.Width, size.Height);
        Bitmap? previous = _decoded;
        WriteableBitmap? front = _front;
        WriteableBitmap? back = _back;
        _decoded = decoded;
        _front = null;
        _back = null;
        SetSource(decoded);
        previous?.Dispose();
        front?.Dispose();
        back?.Dispose();
        SetDetail(_frozen);
    }

    private void Invalidate()
    {
        Bitmap? ready;
        lock (_gate)
        {
            _generation++;
            _pending = null;
            ready = _ready;
            _ready = null;
            _hasReady = false;
        }

        ready?.Dispose();
    }

    private void Release()
    {
        Invalidate();
        DropSource();
    }

    private void DropSource()
    {
        if (Source != null)
        {
            SetSource(source: null);
        }

        _front?.Dispose();
        _back?.Dispose();
        _decoded?.Dispose();
        _front = null;
        _back = null;
        _decoded = null;
    }

    private void SetSource(Bitmap? source)
    {
        Source = source;
        OnPropertyChanged(SourceArgs);
        bool hasFrame = source != null;
        if (hasFrame != HasFrame)
        {
            HasFrame = hasFrame;
            OnPropertyChanged(HasFrameArgs);
            OnPropertyChanged(ShowCoverArgs);
        }
    }

    [RelayCommand]
    private void Recenter() => _send(ModuleId, new CameraControlCommand(StreamId, CameraControlKind.Recenter));

    [RelayCommand]
    private void Recalibrate() => _send(ModuleId, new CameraControlCommand(StreamId, CameraControlKind.Recalibrate));

    internal sealed class Decoding
    {
        public static readonly Decoding Default = new(
            work => ThreadPool.QueueUserWorkItem(static run => run(), work, preferLocal: false),
            work => Dispatcher.UIThread.Post(work),
            DecodeJpeg);

        public Decoding(Action<Action> run, Action<Action> post, Func<ReadOnlyMemory<byte>, Bitmap?> decode)
        {
            Run = run;
            Post = post;
            Decode = decode;
        }

        public Action<Action> Run { get; }

        public Action<Action> Post { get; }

        public Func<ReadOnlyMemory<byte>, Bitmap?> Decode { get; }

        public static Bitmap? DecodeJpeg(ReadOnlyMemory<byte> data)
        {
            try
            {
                using MemoryStream stream = MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment)
                    ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
                    : new MemoryStream(data.ToArray(), writable: false);
                return new Bitmap(stream);
            }
            catch (OutOfMemoryException)
            {
                return null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }
    }
}
