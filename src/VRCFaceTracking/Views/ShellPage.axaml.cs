using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using VRCFaceTracking.Contracts;

namespace VRCFaceTracking.Views;

public partial class ShellPage : UserControl
{
    private readonly MainPage _mainPage = new();
    private readonly OutputPage _outputPage = new();
    private readonly ModuleRegistryPage _moduleRegistryPage = new();
    private readonly MutatorPage _mutatorPage = new();
    private readonly SettingsPage _settingsPage = new();

    private Control? _currentPage;

    public ShellPage()
    {
        InitializeComponent();

        if (!App.EnableMotion)
        {
            PageHost.PageTransition = null;
        }

        NavView.SelectedItem = NavView.MenuItems[0];
        ShowPage(_mainPage);
    }

    private void ShowPage(Control page)
    {
        if (ReferenceEquals(_currentPage, page))
        {
            return;
        }

        if (_currentPage is INotifyNavigated leaving)
        {
            leaving.OnNavigatedFrom();
        }

        _currentPage = page;
        PageHost.Content = page;

        if (page is INotifyNavigated notifyNavigated)
        {
            notifyNavigated.OnNavigatedTo();
        }
    }

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.IsSettingsSelected)
        {
            ShowPage(_settingsPage);
            return;
        }

        if (e.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            ShowPage(tag switch
            {
                "Main" => (Control)_mainPage,
                "Output" => _outputPage,
                "ModuleRegistry" => _moduleRegistryPage,
                "Mutator" => _mutatorPage,
                _ => _mainPage
            });
        }
    }
}