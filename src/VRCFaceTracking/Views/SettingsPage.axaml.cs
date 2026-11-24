using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Contracts;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

internal sealed partial class SettingsPage : UserControl, INotifyNavigated
{
    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private bool _applyingTheme;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SettingsViewModel>();
        SyncThemeCombo();
    }

    public void OnNavigatedTo()
    {
        SyncThemeCombo();
        ViewModel.ReloadOscFields();
        ViewModel.RefreshOpenVrState();
    }

    private void Field_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }
    }

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

    private async void ForceReInit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ViewModel.RiskySettings.ForceReInitAsync(CancellationToken.None);

    private void ResetVRCFT_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.RiskySettings.ResetVRCFT();

    private void ResetAvatarConfig_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.RiskySettings.ResetAvatarOscManifests();

    private async void Link_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
