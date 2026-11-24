using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCFaceTracking.Core.Library;
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
    private byte[] _scratch = [];
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private Bitmap? _decoded;
    private long _lastFrameStamp;
    private long _windowStamp;
    private int _windowCount;
    private int _rate;
    private int _width;
    private int _height;
    private bool _wasFrozen;

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

    public CameraStreamViewModel(CameraStreamSnapshot first, Func<Guid, CameraControlCommand, bool> send, TimeProvider time)
    {
        ModuleId = first.ModuleId;
        StreamId = first.StreamId;
        _send = send;
        _time = time;
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
        Title = TitleFor(snapshot);
        Capabilities = snapshot.Capabilities;
        if (snapshot.Sequence != LastSequence && CameraPixels.IsDrawable(snapshot.Format, snapshot.Width, snapshot.Height, snapshot.Data.Length))
        {
            AcceptFrame(snapshot, frozen, now);
        }

        if (_lastFrameStamp == 0 || _time.GetElapsedTime(_lastFrameStamp, now) >= StaleAfter)
        {
            Clear();
            return false;
        }

        if (!HasFrame)
        {
            HasFrame = true;
            OnPropertyChanged(HasFrameArgs);
            OnPropertyChanged(ShowCoverArgs);
        }

        TimeSpan elapsed = _time.GetElapsedTime(_windowStamp, now);
        if (elapsed >= RateWindow)
        {
            _rate = (int)Math.Round(_windowCount / elapsed.TotalSeconds);
            _windowCount = 0;
            _windowStamp = now;
        }

        SetDetail(frozen);
        _wasFrozen = frozen;
        return true;
    }

    public void Clear()
    {
        Release();
        _lastFrameStamp = 0;
        _windowStamp = 0;
        _windowCount = 0;
        _rate = 0;
        _wasFrozen = false;
        if (HasFrame)
        {
            HasFrame = false;
            OnPropertyChanged(HasFrameArgs);
            OnPropertyChanged(ShowCoverArgs);
        }

        SetText(Resources.CameraStreamsWaiting);
    }

    private static string TitleFor(CameraStreamSnapshot snapshot)
    {
        string legacy = string.Equals(snapshot.StreamId, CameraStreamRegistry.LegacyEyeId, StringComparison.Ordinal)
            ? Resources.CameraStreamsEye
            : Resources.CameraStreamsLip;
        return snapshot.IsLegacy ? legacy : snapshot.Label.Length > 0 ? snapshot.Label : snapshot.StreamId;
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
        _width = snapshot.Width;
        _height = snapshot.Height;
        FrameWidth = Math.Clamp(Math.Round(FrameHeight * snapshot.Width / snapshot.Height), MinFrameWidth, MaxFrameWidth);
        if (!frozen || Source == null)
        {
            Draw(snapshot);
        }
    }

    private void SetDetail(bool frozen)
    {
        string size = TrackingText.Format(Resources.CameraDetail_Size, _width, _height);
        string text = frozen
            ? TrackingText.Format(Resources.CameraDetail_Frozen, _width, _height)
            : _rate > 0
                ? TrackingText.Format(Resources.CameraDetail_Rate, _width, _height, _rate.ToString(CultureInfo.CurrentCulture))
                : size;
        if (frozen != _wasFrozen || !string.Equals(text, Detail, StringComparison.Ordinal))
        {
            SetText(text);
        }
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

    private void Draw(CameraStreamSnapshot snapshot)
    {
        if (snapshot.Format == CameraPixelFormat.Jpeg)
        {
            DrawEncoded(snapshot.Data);
            return;
        }

        if (_decoded != null)
        {
            Release();
        }

        int width = snapshot.Width;
        int height = snapshot.Height;
        var size = new PixelSize(width, height);
        if (_front != null && _front.PixelSize != size)
        {
            Release();
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

        _back = _front;
        _front = target;
        Source = target;
        OnPropertyChanged(SourceArgs);
    }

    private void DrawEncoded(ReadOnlyMemory<byte> data)
    {
        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            var decoded = new Bitmap(stream);
            Release();
            _decoded = decoded;
            Source = decoded;
            OnPropertyChanged(SourceArgs);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException or InvalidDataException)
        {
            Release();
        }
    }

    private void Release()
    {
        Source = null;
        OnPropertyChanged(SourceArgs);
        _front?.Dispose();
        _back?.Dispose();
        _decoded?.Dispose();
        _front = null;
        _back = null;
        _decoded = null;
    }

    [RelayCommand]
    private void Recenter() => _send(ModuleId, new CameraControlCommand(StreamId, CameraControlKind.Recenter));

    [RelayCommand]
    private void Recalibrate() => _send(ModuleId, new CameraControlCommand(StreamId, CameraControlKind.Recalibrate));
}
