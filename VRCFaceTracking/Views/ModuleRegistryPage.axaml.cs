using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ModuleRegistryPage> _logger;

    public ModuleRegistryPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<ModuleRegistryViewModel>();
        _moduleInstaller = Ioc.Default.GetRequiredService<ModuleInstaller>();
        _libManager = Ioc.Default.GetRequiredService<ILibManager>();
        _logger = Ioc.Default.GetRequiredService<ILoggerFactory>().CreateLogger<ModuleRegistryPage>();
    }

    public async void OnNavigatedTo() => await ViewModel.OnNavigatedTo();

    private void ModuleSelection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;
        InstallButton.IsVisible = module.InstallationState != InstallState.Installed;
        UninstallButton.IsVisible = module.InstallationState == InstallState.Installed;
        InstallButton.Content = Strings.Resources.ModuleActionInstall;
        InstallButton.IsEnabled = true;
        if (module.InstallationState != InstallState.AwaitingRestart)
        {
            UninstallButton.IsEnabled = true;
            UninstallButton.Content = Strings.Resources.ModuleActionUninstall;
        }
    }

    private async void InstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;
        InstallButton.IsEnabled = false;
        InstallButton.Content = Strings.Resources.ModuleActionInstalling;

        var installed = false;
        try
        {
            await Task.Run(() => _libManager.TeardownAllAndReset());
            var path = await _moduleInstaller.InstallRemoteModule(module);
            if (path != null)
            {
                module.InstallationState = InstallState.Installed;
                installed = true;
            }
            _libManager.Initialize();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installing module {ModuleId} failed", module.ModuleId);
        }
        finally
        {
            InstallButton.Content = installed ? Strings.Resources.ModuleActionInstalled : Strings.Resources.ModuleActionFailed;
            InstallButton.IsEnabled = true;
        }
    }

    private async void UninstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;

        UninstallButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                _libManager.TeardownAllAndReset();
                _moduleInstaller.UninstallModule(module);
            });
            module.InstallationState = InstallState.NotInstalled;
            _libManager.Initialize();
            await ViewModel.OnNavigatedTo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uninstalling module {ModuleId} failed", module.ModuleId);
        }
        finally
        {
            UninstallButton.IsEnabled = true;
        }
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
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Resources.InstallFromZipTooltip,
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

        if (files.Count == 0) return;

        try
        {
            foreach (var file in files)
            {
                await _moduleInstaller.InstallLocalModule(file.Path.LocalPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installing a module from a zip failed");
        }
        finally
        {
            _libManager.Initialize();
        }
    }
}
