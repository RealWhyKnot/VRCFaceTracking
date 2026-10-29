using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

public sealed class MutatorPageTests
{
    private static readonly string[] CardOrder =
    [
        "Eye Assists",
        "Mouth & Brow Assists",
        "Parameter Adjustment",
        "Calibration",
        "Unified Correctors",
        "Data Filter",
    ];

    private static UnifiedTrackingMutator CreateMutator() =>
        new(NullLogger<UnifiedTrackingMutator>.Instance, App.GetService<ILocalSettingsService>(), App.GetService<IDispatcherService>());

    private static MutatorViewModel CreateViewModel(UnifiedTrackingMutator mutator) =>
        new(mutator, App.GetService<DeveloperSettings>());

    private static void AddMutations(UnifiedTrackingMutator mutator)
    {
        foreach (TrackingMutation mutation in TrackingMutation.GetImplementingMutations())
        {
            mutation.Logger = NullLogger.Instance;
            mutation.CreateProperties();
            mutator.Mutations.Add(mutation);
        }
    }

    private static void RenderUntilIdle()
    {
        for (int frame = 0; frame < 10; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (!Dispatcher.UIThread.HasJobsWithPriority(DispatcherPriority.SystemIdle))
            {
                return;
            }
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void CardsShowAssistsFirstWhenMutationsLoadAfterThePage()
    {
        UnifiedTrackingMutator mutator = CreateMutator();
        MutatorViewModel viewModel = CreateViewModel(mutator);

        AddMutations(mutator);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(CardOrder, viewModel.Mutations.Select(m => m.Name), StringComparer.Ordinal);
        Assert.NotEqual(CardOrder, mutator.Mutations.Select(m => m.Name), StringComparer.Ordinal);
    }

    [AvaloniaFact]
    public void CardsShowAssistsFirstWhenMutationsLoadBeforeThePage()
    {
        UnifiedTrackingMutator mutator = CreateMutator();
        AddMutations(mutator);

        MutatorViewModel viewModel = CreateViewModel(mutator);

        Assert.Equal(CardOrder, viewModel.Mutations.Select(m => m.Name), StringComparer.Ordinal);
    }

    [AvaloniaFact]
    public void CardsClearAndRefillWhenMutationsReload()
    {
        UnifiedTrackingMutator mutator = CreateMutator();
        AddMutations(mutator);
        MutatorViewModel viewModel = CreateViewModel(mutator);

        mutator.Mutations.Clear();
        AddMutations(mutator);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(CardOrder, viewModel.Mutations.Select(m => m.Name), StringComparer.Ordinal);
    }

    [AvaloniaFact]
    public void EyeAssistsGroupsSettingsAndIndentsSubOptionsWithoutMovingControls()
    {
        UnifiedTrackingMutator mutator = CreateMutator();
        AddMutations(mutator);
        var page = new MutatorPage { DataContext = CreateViewModel(mutator) };
        var window = new Window { Content = page, Width = 900, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Expander card = page.GetVisualDescendants().OfType<Expander>().First();
        Assert.Equal("Eye Assists", ((TrackingMutation)card.DataContext!).Name);
        card.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        IEnumerable<string?> headers = card.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Classes.Contains("mutation-group") && t.IsEffectivelyVisible)
            .Select(t => t.Text);
        Assert.Equal(["Gaze", "Eyelids"], headers, StringComparer.Ordinal);

        var rows = card.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("mutation-row") && b.IsEffectivelyVisible)
            .Select(b => (Grid)b.Child!)
            .ToList();
        double LabelX(string name) => rows
            .Select(g => g.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0))
            .Single(t => string.Equals(t.Text, name, StringComparison.Ordinal))
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

    [AvaloniaFact]
    public async Task ResetValuesPutsTheSlidersBackToDefaults()
    {
        UnifiedTrackingMutator mutator = CreateMutator();
        AddMutations(mutator);
        var page = new MutatorPage { DataContext = CreateViewModel(mutator) };
        var window = new Window { Content = page, Width = 900, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Expander card = page.GetVisualDescendants()
            .OfType<Expander>()
            .Single(e => string.Equals(((TrackingMutation)e.DataContext!).Name, "Parameter Adjustment", StringComparison.Ordinal));
        card.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var adjustment = (ParameterAdjustment)card.DataContext!;
        MutationRangeProperty jaw = adjustment.Components.OfType<MutationRangeProperty>().Single(c => string.Equals(c.Name, "Jaw", StringComparison.Ordinal));
        Slider jawCeiling = card.GetVisualDescendants().OfType<Slider>().Single(s => ReferenceEquals(s.DataContext, jaw) && Grid.GetColumn(s) == 2);
        jawCeiling.Value = 0.8;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0f, 0.8f), adjustment.JawOpen);

        Button reset = card.GetVisualDescendants().OfType<Button>().Single(b => string.Equals(b.Content as string, "Reset Values", StringComparison.Ordinal));
        reset.BringIntoView();
        RenderUntilIdle();
        Point center = reset.TranslatePoint(new Point(reset.Bounds.Width / 2, reset.Bounds.Height / 2), window)!.Value;
        Assert.Same(reset, (window.InputHitTest(center) as Visual)?.FindAncestorOfType<Button>(includeSelf: true));
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);

        var waited = Stopwatch.StartNew();
        while (jawCeiling.Value < 1 && waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(1, jawCeiling.Value, 3);
        Assert.Equal((0f, 1f), adjustment.JawOpen);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(600)]
    public void EveryCardFitsTheWindowWhenNarrow(double width)
    {
        UnifiedTrackingMutator mutator = CreateMutator();
        AddMutations(mutator);
        var page = new MutatorPage { DataContext = CreateViewModel(mutator) };
        var window = new Window { Content = page, Width = width, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (Expander? expander in page.GetVisualDescendants().OfType<Expander>().ToList())
        {
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
        }

        ScrollViewer scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
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
