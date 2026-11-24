using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;

namespace VRCFaceTracking.ViewModels;

internal sealed partial class CameraStreamViewModel : ObservableObject
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(1);

    private static readonly PropertyChangedEventArgs SourceArgs = new(nameof(Source));
    private static readonly PropertyChangedEventArgs HasFrameArgs = new(nameof(HasFrame));
    private static readonly PropertyChangedEventArgs ShowCoverArgs = new(nameof(ShowCover));
    private static readonly PropertyChangedEventArgs DetailArgs = new(nameof(Detail));

    private readonly TimeProvider _time;
    private byte[]? _lastData;
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
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

    public CameraStreamViewModel(string title, TimeProvider time)
    {
        Title = title;
        _time = time;
        Detail = Resources.CameraStreamsWaiting;
    }

    public string Title { get; }

    public Bitmap? Source { get; private set; }

    public bool HasFrame { get; private set; }

    public bool ShowCover => IsBlurred && HasFrame;

    public string Detail { get; private set; }

    public bool Update(Core.Types.Image? frame, bool frozen)
    {
        long now = _time.GetTimestamp();
        byte[]? data = frame?.ImageData;
        if (frame is { SupportsImage: true } && data != null && !ReferenceEquals(data, _lastData))
        {
            (int x, int y) = frame.ImageSize;
            if (x > 0 && y > 0 && data.Length >= x * y * 4)
            {
                if (_lastFrameStamp == 0 || _windowStamp == 0)
                {
                    _windowStamp = now;
                    _windowCount = 0;
                }

                _lastData = data;
                _lastFrameStamp = now;
                _windowCount++;
                _width = x;
                _height = y;
                if (!frozen || Source == null)
                {
                    Draw(data, x, y);
                }
            }
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
        _lastData = null;
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

    private void SetDetail(bool frozen)
    {
        string text = frozen
            ? TrackingText.Format(Resources.CameraDetail_Frozen, _width, _height)
            : _rate > 0
                ? TrackingText.Format(Resources.CameraDetail_Rate, _width, _height, _rate.ToString(CultureInfo.CurrentCulture))
                : TrackingText.Format(Resources.CameraDetail_Size, _width, _height);
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

    private void Draw(byte[] data, int width, int height)
    {
        var size = new PixelSize(width, height);
        if (_front != null && _front.PixelSize != size)
        {
            Release();
        }

        WriteableBitmap target = _back ?? new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer buffer = target.Lock())
        {
            if (buffer.RowBytes == width * 4)
            {
                Marshal.Copy(data, 0, buffer.Address, width * height * 4);
            }
            else
            {
                for (int row = 0; row < height; row++)
                {
                    Marshal.Copy(data, row * width * 4, buffer.Address + (row * buffer.RowBytes), width * 4);
                }
            }
        }

        _back = _front;
        _front = target;
        Source = target;
        OnPropertyChanged(SourceArgs);
    }

    private void Release()
    {
        Source = null;
        OnPropertyChanged(SourceArgs);
        _front?.Dispose();
        _back?.Dispose();
        _front = null;
        _back = null;
    }
}
