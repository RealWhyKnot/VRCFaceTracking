using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public class MutatorPageTests
{
    private static readonly string[] CardOrder =
    {
        "Eye Assists",
        "Mouth & Brow Assists",
        "Parameter Adjustment",
        "Calibration",
        "Unified Correctors",
        "Data Filter",
    };

    private static UnifiedTrackingMutator CreateMutator() =>
        new(NullLogger<UnifiedTrackingMutator>.Instance, App.GetService<ILocalSettingsService>(), App.GetService<IDispatcherService>());

    private static MutatorViewModel CreateViewModel(UnifiedTrackingMutator mutator) =>
        new(mutator, App.GetService<DeveloperSettings>(), App.GetService<ILocalSettingsService>());

    private static void AddMutations(UnifiedTrackingMutator mutator)
    {
        foreach (var mutation in TrackingMutation.GetImplementingMutations())
        {
            mutation.Logger = NullLogger.Instance;
            mutation.CreateProperties();
            mutator._mutations.Add(mutation);
        }
    }

    [AvaloniaFact]
    public void Cards_ShowAssistsFirst_WhenMutationsLoadAfterThePage()
    {
        var mutator = CreateMutator();
        var viewModel = CreateViewModel(mutator);

        AddMutations(mutator);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(CardOrder, viewModel.Mutations.Select(m => m.Name));
        Assert.NotEqual(CardOrder, mutator._mutations.Select(m => m.Name));
    }

    [AvaloniaFact]
    public void Cards_ShowAssistsFirst_WhenMutationsLoadBeforeThePage()
    {
        var mutator = CreateMutator();
        AddMutations(mutator);

        var viewModel = CreateViewModel(mutator);

        Assert.Equal(CardOrder, viewModel.Mutations.Select(m => m.Name));
    }

    [AvaloniaFact]
    public void Cards_ClearAndRefill_WhenMutationsReload()
    {
        var mutator = CreateMutator();
        AddMutations(mutator);
        var viewModel = CreateViewModel(mutator);

        mutator._mutations.Clear();
        AddMutations(mutator);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(CardOrder, viewModel.Mutations.Select(m => m.Name));
    }

    [AvaloniaFact]
    public void EyeAssists_GroupsSettings_AndIndentsSubOptions_WithoutMovingControls()
    {
        var mutator = CreateMutator();
        AddMutations(mutator);
        var page = new MutatorPage { DataContext = CreateViewModel(mutator) };
        var window = new Window { Content = page, Width = 900, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = page.GetVisualDescendants().OfType<Expander>().First();
        Assert.Equal("Eye Assists", ((TrackingMutation)card.DataContext!).Name);
        card.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var headers = card.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Classes.Contains("mutation-group") && t.IsEffectivelyVisible)
            .Select(t => t.Text);
        Assert.Equal(new[] { "Gaze", "Eyelids" }, headers);

        var rows = card.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("mutation-row") && b.IsEffectivelyVisible)
            .Select(b => (Grid)b.Child!)
            .ToList();
        double LabelX(string name) => rows
            .Select(g => g.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0))
            .Single(t => t.Text == name)
            .TranslatePoint(new Point(0, 0), card)!.Value.X;

        Assert.Equal(LabelX("Eyelid Sync") + 20, LabelX("Eyelid Sync Strength"), 1);
        Assert.Equal(LabelX("Gaze Convergence Fix"), LabelX("Eye Close Assist"), 1);

        var controlColumns = rows
            .Select(g => g.TranslatePoint(new Point(g.ColumnDefinitions[0].ActualWidth, 0), card)!.Value.X)
            .Distinct()
            .ToList();
        Assert.Single(controlColumns);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(600)]
    public void EveryCard_FitsTheWindow_WhenNarrow(double width)
    {
        var mutator = CreateMutator();
        AddMutations(mutator);
        var page = new MutatorPage { DataContext = CreateViewModel(mutator) };
        var window = new Window { Content = page, Width = width, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (var expander in page.GetVisualDescendants().OfType<Expander>().ToList())
        {
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
        }

        var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
        var content = (Control)scroll.Content!;
        var overflow = content.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && c is TextBlock or Slider or CheckBox or Button)
            .Select(c => (c, right: c.TranslatePoint(new Point(c.Bounds.Width, 0), content)!.Value.X))
            .Where(x => x.right > content.Bounds.Width + 0.5)
            .Select(x => x.c.GetType().Name + " " + (x.c as TextBlock)?.Text)
            .ToList();

        Assert.Empty(overflow);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
