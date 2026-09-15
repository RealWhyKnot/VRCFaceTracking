using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using VRCFaceTracking.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

public partial class ModuleRegistryPage : UserControl, INotifyNavigated
{
    private ModuleRegistryViewModel ViewModel => (ModuleRegistryViewModel)DataContext!;
    private readonly ModuleInstaller _moduleInstaller;
    private readonly ILibManager _libManager;

    public ModuleRegistryPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<ModuleRegistryViewModel>();
        _moduleInstaller = Ioc.Default.GetRequiredService<ModuleInstaller>();
        _libManager = Ioc.Default.GetRequiredService<ILibManager>();
    }

    public async void OnNavigatedTo() => await ViewModel.OnNavigatedTo();

    private async void ModuleSelection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;
        InstallButton.IsVisible = module.InstallationState != InstallState.Installed;
        UninstallButton.IsVisible = module.InstallationState == InstallState.Installed;
        InstallButton.Content = "Install";
        InstallButton.IsEnabled = true;
        if (module.InstallationState != InstallState.AwaitingRestart)
        {
            UninstallButton.IsEnabled = true;
            UninstallButton.Content = "Uninstall";
        }
    }
    private async void InstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;
        InstallButton.IsEnabled = false;
        InstallButton.Content = "Installing...";

        try
        {
            await Task.Run(() => _libManager.TeardownAllAndResetAsync());
            var path = await _moduleInstaller.InstallRemoteModule(module);
            if (path != null)
            {
                module.InstallationState = InstallState.Installed;
            }
            _libManager.Initialize();
        }
        finally
        {
            InstallButton.Content = "Installed.";
        }
    }

    private async void UninstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;

        UninstallButton.IsEnabled = false;
        await Task.Run(() =>
        {
            _libManager.TeardownAllAndResetAsync();
            _moduleInstaller.UninstallModule(module);
        });
        module.InstallationState = InstallState.NotInstalled;
        _libManager.Initialize();
        await ViewModel.OnNavigatedTo();
    }

    private async void OpenModulePage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected?.ModulePageUrl is not { Length: > 0 } url) return;

        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher != null)
                await launcher.LaunchUriAsync(new Uri(url));
        }
        catch { }
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Install from .zip",
            AllowMultiple = false,
            FileTypeFilter = [
                new FilePickerFileType("Zip Files")
                {
                    Patterns = (IReadOnlyList<string>)
                    [
                        "*.zip"
                    ],
                    AppleUniformTypeIdentifiers = (IReadOnlyList<string>)
                    [
                        "public.zip"
                    ],
                    MimeTypes = (IReadOnlyList<string>)
                        [
                            "application/zip",
                            "application/x-zip",
                            "application/x-zip-compressed",
                            "application/zip-compressed",
                            "multipart/x-zip"
                        ]

                }
            ]
        });

        try
        {
            foreach (var file in files)
            {
                await _moduleInstaller.InstallLocalModule(file.Path.LocalPath);
            }
        }
        finally
        {
            _libManager.Initialize();
        }
    }
}
