using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

internal sealed partial class SettingsPage : UserControl, INotifyNavigated
{
    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private readonly StreamView _eyeStream;
    private readonly StreamView _lipStream;
    private DispatcherTimer? _streamTimer;
    private DateTime _streamStart;
    private bool _applyingTheme;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SettingsViewModel>();


        SyncThemeCombo();

        _eyeStream = new StreamView(EyeStreamImage, EyeStreamStatus);
        _lipStream = new StreamView(LipStreamImage, LipStreamStatus);
        CameraStreamsExpander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty)
            {
                OnCameraStreamsToggled((bool)e.NewValue!);
            }
        };
        Unloaded += (_, _) => OnCameraStreamsToggled(enabled: false);
    }

    public void OnNavigatedTo()
    {
        SyncThemeCombo();
        ViewModel.RefreshOpenVrState();
    }

    public void OnNavigatedFrom() => CameraStreamsExpander.IsExpanded = false;

    private void SyncThemeCombo()
    {
        _applyingTheme = true;
        try
        {
            ThemeCombo.SelectedIndex = Application.Current?.RequestedThemeVariant?.Key?.ToString() switch
            {
                "Light" => 0,
                "Dark" => 1,
                _ => 2,
            };
        }
        finally
        {
            _applyingTheme = false;
        }
    }

    private void ThemeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingTheme)
        {
            return;
        }

        if (ThemeCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        string tag = item.Tag?.ToString() ?? "Default";
        Application.Current!.RequestedThemeVariant = tag switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        _ = ViewModel.SaveThemeAsync(tag, CancellationToken.None);
    }

    private async void ChangeLogFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IStorageProvider? storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFolder> folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Strings.Resources.LogFolderPickerTitle,
                AllowMultiple = false,
            });

            string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                await ViewModel.SetLogDirectoryAsync(path, CancellationToken.None);
            }
        }
        catch (COMException ex)
        {
            LogChangingLogFolderFailed(Ioc.Default.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsPage>(), ex);
        }
    }

    private void OnCameraStreamsToggled(bool enabled)
    {
        Ioc.Default.GetRequiredService<ILibManager>().SetImageStreamEnabled(enabled);
        if (enabled)
        {
            _streamStart = DateTime.UtcNow;
            _streamTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, RenderStreams);
            _streamTimer.Start();
        }
        else
        {
            _streamTimer?.Stop();
            _eyeStream.Reset();
            _lipStream.Reset();
        }
    }

    private void RenderStreams(object? sender, EventArgs e)
    {
        _eyeStream.Render(UnifiedTracking.EyeImageData, _streamStart);
        _lipStream.Render(UnifiedTracking.LipImageData, _streamStart);
    }

    private sealed class StreamView
    {
        private readonly Image _image;
        private readonly TextBlock _status;
        private byte[]? _lastData;

        public StreamView(Image image, TextBlock status)
        {
            _image = image;
            _status = status;
        }

        public void Reset()
        {
            ReleaseBitmap();
            _image.IsVisible = true;
            _lastData = null;
            _status.Text = Strings.Resources.CameraStreamsWaiting;
            _status.IsVisible = true;
        }

        private void ReleaseBitmap()
        {
            var old = _image.Source as IDisposable;
            _image.Source = null;
            old?.Dispose();
        }

        public void Render(Core.Types.Image frame, DateTime start)
        {
            byte[] data = frame.ImageData;
            if (data == null || !frame.SupportsImage)
            {
                if (DateTime.UtcNow - start >= TimeSpan.FromSeconds(2))
                {
                    _status.Text = Strings.Resources.CameraStreamsNone;
                    _status.IsVisible = true;
                    _image.IsVisible = false;
                }
                return;
            }

            if (ReferenceEquals(data, _lastData))
            {
                return;
            }

            _lastData = data;

            (int x, int y) = frame.ImageSize;
            if (x <= 0 || y <= 0 || data.Length < x * y * 4)
            {
                return;
            }

            if (_image.Source is not WriteableBitmap bitmap || bitmap.PixelSize.Width != x || bitmap.PixelSize.Height != y)
            {
                ReleaseBitmap();
                bitmap = new WriteableBitmap(new PixelSize(x, y), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                _image.Source = bitmap;
            }

            using (ILockedFramebuffer fb = bitmap.Lock())
            {
                if (fb.RowBytes == x * 4)
                {
                    Marshal.Copy(data, 0, fb.Address, x * y * 4);
                }
                else
                {
                    for (int row = 0; row < y; row++)
                    {
                        Marshal.Copy(data, row * x * 4, fb.Address + (row * fb.RowBytes), x * 4);
                    }
                }
            }

            _image.InvalidateVisual();
            _image.IsVisible = true;
            _status.IsVisible = false;
        }
    }

    private async void ForceReInit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ViewModel.RiskySettings.ForceReInitAsync(CancellationToken.None);

    private void ResetVRCFT_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.RiskySettings.ResetVRCFT();

    private void ResetAvatarConfig_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.RiskySettings.ResetAvatarOscManifests();

    private async void ContributorButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ILauncher? launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (sender is Button { Tag: string url } && launcher != null && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            await launcher.LaunchUriAsync(uri);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Changing the log folder failed")]
    private static partial void LogChangingLogFolderFailed(ILogger logger, Exception exception);
}
