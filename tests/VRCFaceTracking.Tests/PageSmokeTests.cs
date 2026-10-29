using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class PageSmokeTests
{
    private static Window ShowInWindow(Control page)
    {
        var window = new Window { Content = page, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void ShowAndClose(Control page)
    {
        Window window = ShowInWindow(page);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MainPageRenders() => ShowAndClose(new MainPage());

    [AvaloniaFact]
    public void OutputPageRenders() => ShowAndClose(new OutputPage());

    [AvaloniaFact]
    public void ModuleRegistryPageRenders() => ShowAndClose(new ModuleRegistryPage());

    [AvaloniaFact]
    public void MutatorPageRenders() => ShowAndClose(new MutatorPage());

    [AvaloniaFact]
    public void SettingsPageRenders() => ShowAndClose(new SettingsPage());

    [AvaloniaFact]
    public void ShellPageNavigatesToEveryPage()
    {
        var shell = new ShellPage();
        Window window = ShowInWindow(shell);

        NavigationView nav = shell.GetVisualDescendants().OfType<NavigationView>().Single();
        TransitioningContentControl host = shell.GetVisualDescendants().OfType<TransitioningContentControl>().Single();
        var seen = new List<Type>();

        foreach (NavigationViewItem? item in nav.MenuItems.OfType<NavigationViewItem>().ToList())
        {
            nav.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();

            Control content = Assert.IsAssignableFrom<Control>(host.Content);
            seen.Add(content.GetType());
        }

        Assert.Equal(4, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MutatorPageRendersMutationComponentsWithDeveloperGating()
    {
        var page = new MutatorPage();
        var viewModel = (MutatorViewModel)page.DataContext!;

        foreach (TrackingMutation mutation in TrackingMutation.GetImplementingMutations())
        {
            mutation.Logger = NullLogger.Instance;
            mutation.CreateProperties();
            viewModel.Mutations.Add(mutation);
        }

        Window window = ShowInWindow(page);

        foreach (Expander? expander in page.GetVisualDescendants().OfType<Expander>().ToList())
        {
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
        }

        Assert.NotEmpty(page.GetVisualDescendants().OfType<Slider>());
        Assert.NotEmpty(page.GetVisualDescendants().OfType<CheckBox>());

        viewModel.Developer.Enabled = true;
        Dispatcher.UIThread.RunJobs();
        viewModel.Developer.Enabled = false;
        Dispatcher.UIThread.RunJobs();

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
