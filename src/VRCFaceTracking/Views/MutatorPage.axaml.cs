using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CommunityToolkit.Mvvm.DependencyInjection;
using VRCFaceTracking.Contracts;
using VRCFaceTracking.ViewModels;

namespace VRCFaceTracking.Views;

internal sealed partial class MutatorPage : UserControl, INotifyNavigated
{
    public MutatorPage()
        : this(Ioc.Default.GetRequiredService<MutatorViewModel>())
    {
    }

    public MutatorPage(MutatorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private MutatorViewModel? ViewModel => DataContext as MutatorViewModel;

    public void OnNavigatedTo() => ViewModel?.Start();

    public void OnNavigatedFrom() => ViewModel?.Stop();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ViewModel?.Stop();
    }

    private static void CloseFlyout(object? sender)
    {
        if (sender is Control control && control.FindLogicalAncestorOfType<Popup>() is { } popup)
        {
            popup.Close();
        }
    }

    private void OnConfirmCancel(object? sender, RoutedEventArgs e) => CloseFlyout(sender);

    private void OnConfirmRun(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionRowViewModel action })
        {
            action.Command.Execute(parameter: null);
        }

        CloseFlyout(sender);
    }
}
