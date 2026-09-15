using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.DependencyInjection;
using FluentAvalonia.Styling;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

public partial class SettingsPage : UserControl
{
    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SettingsViewModel>();

        VersionText.Text = SettingsViewModel.VersionText;

        ThemeCombo.SelectedIndex = Application.Current?.RequestedThemeVariant?.Key?.ToString() switch
        {
            "Light" => 0,
            "Dark" => 1,
            _ => 2
        };
    }

    private void ThemeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
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
