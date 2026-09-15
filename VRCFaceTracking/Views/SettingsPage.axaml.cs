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

public partial class SettingsPage : UserControl, INotifyNavigated
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

        VersionText.Text = SettingsViewModel.VersionText;

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
        Unloaded += (_, _) => OnCameraStreamsToggled(false);
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
                _ => 2
            };
        }
        finally
        {
            _applyingTheme = false;
        }
    }

    private void ThemeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingTheme) return;
        if (ThemeCombo.SelectedItem is not ComboBoxItem item) return;

        var tag = item.Tag?.ToString() ?? "Default";
        Application.Current!.RequestedThemeVariant = tag switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        _ = ViewModel.SaveThemeAsync(tag);
    }

    private async void ChangeLogFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Strings.Resources.LogFolderPickerTitle,
                AllowMultiple = false
            });

            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                await ViewModel.SetLogDirectoryAsync(path);
            }
        }
        catch (Exception ex)
        {
            Ioc.Default.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsPage>().LogError(ex, "Changing the log folder failed");
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

    private sealed class StreamView(Image image, TextBlock status)
    {
        private WriteableBitmap? _bitmap;
        private byte[]? _lastData;

        public void Reset()
        {
            image.Source = null;
            image.IsVisible = true;
            _bitmap = null;
            _lastData = null;
            status.Text = Strings.Resources.CameraStreamsWaiting;
            status.IsVisible = true;
        }

        public void Render(Core.Types.Image frame, DateTime start)
        {
            var data = frame.ImageData;
            if (data == null || !frame.SupportsImage)
            {
                if (DateTime.UtcNow - start >= TimeSpan.FromSeconds(2))
                {
                    status.Text = Strings.Resources.CameraStreamsNone;
                    status.IsVisible = true;
                    image.IsVisible = false;
                }
                return;
            }

            if (ReferenceEquals(data, _lastData)) return;
            _lastData = data;

            var (x, y) = frame.ImageSize;
            if (x <= 0 || y <= 0 || data.Length < x * y * 4) return;

            if (_bitmap == null || _bitmap.PixelSize.Width != x || _bitmap.PixelSize.Height != y)
            {
                _bitmap = new WriteableBitmap(new PixelSize(x, y), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                image.Source = _bitmap;
            }

            using (var fb = _bitmap.Lock())
            {
                if (fb.RowBytes == x * 4)
                {
                    Marshal.Copy(data, 0, fb.Address, x * y * 4);
                }
                else
                {
                    for (var row = 0; row < y; row++)
                    {
                        Marshal.Copy(data, row * x * 4, fb.Address + row * fb.RowBytes, x * 4);
                    }
                }
            }

            image.InvalidateVisual();
            image.IsVisible = true;
            status.IsVisible = false;
        }
    }

    private void ForceReInit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel.RiskySettings.ForceReInit();
    }

    private void ResetVRCFT_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel.RiskySettings.ResetVRCFT();
    }

    private void ResetAvatarConfig_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel.RiskySettings.ResetAvatarOscManifests();
    }

    private async void ContributorButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrEmpty(url))
        {
            try
            {
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher != null)
                    await launcher.LaunchUriAsync(new Uri(url));
            }
            catch { }
        }
    }
}
