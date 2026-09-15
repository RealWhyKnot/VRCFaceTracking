using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.DependencyInjection;
using VRCFaceTracking.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

public partial class MainPage : UserControl, INotifyNavigated
{
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    public MainPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
    }

    public void OnNavigatedTo() => ViewModel.OnNavigatedTo();

    private void RestartModules_OnClick(object? sender, RoutedEventArgs e) =>
        Ioc.Default.GetRequiredService<ILibManager>().Initialize();

    private void ResetCapabilities_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ModuleMetadataInternal metadata })
        {
            metadata.ResetCapabilitiesToAutomatic();
        }
    }
}