using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.UiTests;

public class PageSmokeTests
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
        var window = ShowInWindow(page);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MainPage_Renders() => ShowAndClose(new MainPage());

    [AvaloniaFact]
    public void OutputPage_Renders() => ShowAndClose(new OutputPage());

    [AvaloniaFact]
    public void ModuleRegistryPage_Renders() => ShowAndClose(new ModuleRegistryPage());

    [AvaloniaFact]
    public void MutatorPage_Renders() => ShowAndClose(new MutatorPage());

    [AvaloniaFact]
    public void SettingsPage_Renders() => ShowAndClose(new SettingsPage());

    [AvaloniaFact]
    public void ShellPage_RendersEveryPage()
    {
        var shell = new ShellPage();
        var window = ShowInWindow(shell);

        foreach (var page in shell.GetVisualDescendants().OfType<UserControl>().ToList())
        {
            page.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MutatorPage_RendersMutationComponents_WithDeveloperGating()
    {
        var page = new MutatorPage();
        var viewModel = (MutatorViewModel)page.DataContext!;

        foreach (var mutation in TrackingMutation.GetImplementingMutations())
        {
            mutation.Logger = NullLogger.Instance;
            mutation.CreateProperties();
            viewModel.Mutations.Add(mutation);
        }

        var window = ShowInWindow(page);

        foreach (var expander in page.GetVisualDescendants().OfType<Expander>().ToList())
        {
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
        }

        Assert.NotEmpty(page.GetVisualDescendants().OfType<Slider>());
        Assert.NotEmpty(page.GetVisualDescendants().OfType<CheckBox>());

        viewModel.DeveloperMode = true;
        Dispatcher.UIThread.RunJobs();
        viewModel.DeveloperMode = false;
        Dispatcher.UIThread.RunJobs();

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
