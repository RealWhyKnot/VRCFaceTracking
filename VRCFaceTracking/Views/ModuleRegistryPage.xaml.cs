using Microsoft.UI.Xaml;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.ViewModels;
using Windows.Storage.Pickers;

namespace VRCFaceTracking.Views;

public sealed partial class ModuleRegistryPage
{
    public ModuleRegistryViewModel ViewModel
    {
        get;
    }

    private ModuleInstaller ModuleInstaller
    {
        get;
    }

    private ILibManager LibManager
    {
        get;
    }

    public ModuleRegistryPage()
    {
        ViewModel = App.GetService<ModuleRegistryViewModel>();
        ModuleInstaller = App.GetService<ModuleInstaller>();
        LibManager = App.GetService<ILibManager>();
        InitializeComponent();
        ViewModel.ModuleInfos.CollectionChanged += (_, _) => ViewModel.EnsureItemSelected();
    }

    private async void InstallCustomModule_OnClick(object sender, RoutedEventArgs e)
    {
        CustomInstallStatus.Text = "";

        var openPicker = new FileOpenPicker();
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);
        openPicker.ViewMode = PickerViewMode.Thumbnail;
        openPicker.FileTypeFilter.Add(".zip");

        var file = await openPicker.PickSingleFileAsync();
        if (file == null)
        {
            CustomInstallStatus.Text = "InstallCustomModuleCancelled".GetLocalized();
            return;
        }

        string? path = null;
        try
        {
            path = await ModuleInstaller.InstallLocalModule(file.Path);
        }
        finally
        {
            if (path != null)
            {
                CustomInstallStatus.Text = "InstallCustomModuleSucceeded".GetLocalized();
                App.MainWindow.DispatcherQueue.TryEnqueue(() => LibManager.Initialize());
            }
            else
            {
                CustomInstallStatus.Text = "InstallCustomModuleFailed".GetLocalized();
            }
        }
    }
}
