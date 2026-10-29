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
        => ViewModel.RefreshActionState();

    private async void InstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;

        ViewModel.IsBusy = true;
        ViewModel.InstallButtonText = Strings.Resources.ModuleActionInstalling;

        try
        {
            await Task.Run(() => _libManager.TeardownAllAndReset());
            var path = await _moduleInstaller.InstallRemoteModule(module);
            if (path != null)
            {
                module.InstallationState = InstallState.Installed;
            }
            _libManager.Initialize();
            await ViewModel.OnNavigatedTo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installing module {ModuleId} failed", module.ModuleId);
        }
        finally
        {
            ViewModel.RefreshActionState();
        }
    }

    private async void UninstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module) return;

        ViewModel.IsBusy = true;
        ViewModel.UninstallButtonText = Strings.Resources.ModuleActionUninstalling;

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
            ViewModel.RefreshActionState();
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
