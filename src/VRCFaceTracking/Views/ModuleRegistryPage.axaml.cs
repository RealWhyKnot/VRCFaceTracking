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

internal sealed partial class ModuleRegistryPage : UserControl, INotifyNavigated
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

    public void OnNavigatedTo() => _ = ViewModel.LoadModulesAsync(CancellationToken.None);

    private void ModuleSelection_Click(object? sender, RoutedEventArgs e)
        => ViewModel.RefreshActionState();

    private async void InstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module)
        {
            return;
        }

        ViewModel.IsBusy = true;
        ViewModel.InstallButtonText = Strings.Resources.ModuleActionInstalling;

        try
        {
            await Task.Run(_libManager.TeardownAllAndReset);
            string? path = await _moduleInstaller.InstallRemoteModuleAsync(module, CancellationToken.None);
            if (path != null)
            {
                module.InstallationState = InstallState.Installed;
            }
            _libManager.Initialize();
            await ViewModel.LoadModulesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogInstallingModuleFailed(_logger, ex, module.ModuleId);
        }
        finally
        {
            ViewModel.RefreshActionState();
        }
    }

    private async void UninstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not InstallableTrackingModule module)
        {
            return;
        }

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
            await ViewModel.LoadModulesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUninstallingModuleFailed(_logger, ex, module.ModuleId);
        }
        finally
        {
            ViewModel.RefreshActionState();
        }
    }

    private async void OpenModulePage_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected?.ModulePageUrl is not { IsAbsoluteUri: true } uri)
        {
            return;
        }

        ILauncher? launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher != null)
        {
            await launcher.LaunchUriAsync(uri);
        }
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Resources.InstallFromZipTooltip,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Zip Files")
                {
                    Patterns = ["*.zip"],
                    AppleUniformTypeIdentifiers = ["public.zip"],
                    MimeTypes =
                    [
                        "application/zip",
                        "application/x-zip",
                        "application/x-zip-compressed",
                        "application/zip-compressed",
                        "multipart/x-zip",
                    ],
                },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            foreach (IStorageFile file in files)
            {
                await _moduleInstaller.InstallLocalModuleAsync(file.Path.LocalPath, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Newtonsoft.Json.JsonException)
        {
            LogInstallingModuleZipFailed(_logger, ex);
        }
        finally
        {
            _libManager.Initialize();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Installing module {ModuleId} failed")]
    private static partial void LogInstallingModuleFailed(ILogger logger, Exception exception, Guid moduleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Uninstalling module {ModuleId} failed")]
    private static partial void LogUninstallingModuleFailed(ILogger logger, Exception exception, Guid moduleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Installing a module from a zip failed")]
    private static partial void LogInstallingModuleZipFailed(ILogger logger, Exception exception);
}
