using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using VRCFaceTracking.Controls;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed partial class MutatorPageTests
{
    [AvaloniaFact]
    public void UndeclaredMutationLandsInALabeledFallbackCard()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load(new UndeclaredMutation());
        TrackingCardViewModel? other = model.FindCard(TrackingCard.Other);
        Assert.NotNull(other);
        Assert.Equal(Resources.TrackingCard_Other_Title, other!.Title);
        Assert.Contains(other.Rows, r => string.Equals(r.Label, "Lone Toggle", StringComparison.Ordinal));
        Assert.Contains(other.MainSections, s => string.Equals(s.Title, "Undeclared Test Mutation", StringComparison.Ordinal));
        Assert.DoesNotContain(CardOrder.Where(c => c != TrackingCard.Advanced), c => model.FindCard(c)!.Rows.Any(r => ReferenceEquals(r.Unit, mutator.Mutations.OfType<UndeclaredMutation>().Single())));
    }

    [AvaloniaFact]
    public void SavedSettingsKeepTheirNamesAndRoundTrip()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        Assert.Equal(SavedNames, mutator.Mutations.Select(m => m.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);

        Toggle(model, TrackingCard.Eyes, nameof(EyeAssists.EyelidSync)).IsChecked = false;
        RangeRowViewModel jaw = Ranges(model).First(r => string.Equals(r.Metadata?.Key, nameof(ParameterAdjustment.JawOpen), StringComparison.Ordinal));
        jaw.Upper = 0.7;

        foreach (TrackingMutation mutation in mutator.Mutations)
        {
            string json = JsonConvert.SerializeObject(mutation);
            var fresh = (TrackingMutation)Activator.CreateInstance(mutation.GetType())!;
            JsonConvert.PopulateObject(json, fresh);
            Assert.Equal(json, JsonConvert.SerializeObject(fresh));
            Assert.Equal(mutation.Name, fresh.Name);
        }

        Assert.Equal((0f, 0.7f), Unit<ParameterAdjustment>(mutator).JawOpen);
        Assert.False(Unit<EyeAssists>(mutator).EyelidSync);
    }

    [AvaloniaFact]
    public void TheDuplicateDeveloperCheckboxIsGone()
    {
        Assert.Null(Resources.ResourceManager.GetString("ShowDeveloperSettingsContent", System.Globalization.CultureInfo.InvariantCulture));
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        var page = new MutatorPage(model);
        Window window = Show(page);
        Assert.Empty(page.GetVisualDescendants().OfType<CheckBox>());
        Close(window);
    }

    [AvaloniaFact]
    public void LivePollingRunsOnlyWhileThePageIsShown()
    {
        (UnifiedTrackingMutator _, MutatorViewModel model) = Load();
        var page = new MutatorPage(model);
        Window window = Show(page);
        Assert.False(model.IsRunning);

        page.OnNavigatedTo();
        Assert.True(model.IsRunning);
        page.OnNavigatedFrom();
        Assert.False(model.IsRunning);

        page.OnNavigatedTo();
        Close(window);
        Assert.False(model.IsRunning);
    }

    [AvaloniaFact]
    public void NoDataShowsAQuietWaitingState()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        model.RefreshStatus();
        Assert.False(model.HasData);
        Assert.Equal(Resources.TrackingActivity_Waiting, model.ActivityText);
        Assert.All(model.Channels, c => Assert.Equal(c.Raw, c.Adjusted));
    }

    [AvaloniaTheory]
    [InlineData(1280, 800)]
    [InlineData(900, 600)]
    public void EverythingFitsAtTheSupportedSizes(double width, double height)
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        model.Developer.Enabled = true;
        foreach (TrackingCardViewModel card in model.Cards)
        {
            card.IsMoreOpen = true;
            foreach (SettingSectionViewModel section in card.MoreSections)
            {
                section.IsExpanded = true;
            }
        }

        var page = new MutatorPage(model);
        Window window = Show(page, width, height);
        AssertNothingSpillsSideways(page);
        model.Developer.Enabled = false;
        Close(window);
    }

    [AvaloniaFact]
    public void LongNamesAndManySettingsDoNotBreakTheLayout()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load(new WideMutation(), new UndeclaredMutation());
        Prepare(mutator);
        foreach (TrackingCardViewModel card in model.Cards)
        {
            card.IsMoreOpen = true;
        }

        var page = new MutatorPage(model);
        Window window = Show(page, 900, 600);
        AssertNothingSpillsSideways(page);

        TrackingCardViewModel other = model.FindCard(TrackingCard.Other)!;
        Assert.True(other.Rows.Count >= 37);
        ScrollViewer scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Close(window);
    }

    [AvaloniaFact]
    public void GermanLengthLabelsStayInsideTheirCards()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        var page = new MutatorPage(model);
        Window window = Show(page, 900, 600);

        foreach (TextBlock block in page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("tracking-row-label")))
        {
            block.Text = "Verlängerte Beschriftung mit außergewöhnlich vielen Zeichen und Umlauten";
        }

        foreach (TextBlock block in page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("tracking-card-description")))
        {
            block.Text = "Eine deutlich längere Beschreibung, die ungefähr dreißig Prozent mehr Platz braucht";
        }

        Settle();
        AssertNothingSpillsSideways(page);
        Close(window);
    }

    [AvaloniaFact]
    public void ControlsShareOneHeightAndHaveNames()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        foreach (TrackingCardViewModel card in model.Cards)
        {
            card.IsMoreOpen = true;
            foreach (SettingSectionViewModel section in card.MoreSections)
            {
                section.IsExpanded = true;
            }
        }

        var page = new MutatorPage(model);
        Window window = Show(page);

        Control[] inputs =
        [
            .. page.GetVisualDescendants().OfType<Slider>().Where(c => c.IsEffectivelyVisible),
            .. page.GetVisualDescendants().OfType<NumberField>().Where(c => c.IsEffectivelyVisible),
            .. page.GetVisualDescendants().OfType<RangeSlider>().Where(c => c.IsEffectivelyVisible),
        ];
        Assert.NotEmpty(inputs);
        Assert.All(inputs, c => Assert.Equal(32, c.Bounds.Height, 1));
        Assert.All(inputs.OfType<NumberField>(), c => Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(c))));
        Assert.All(page.GetVisualDescendants().OfType<ToggleSwitch>().Where(t => t.IsEffectivelyVisible), t => Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(t))));
        Close(window);
    }

    private static void AssertNothingSpillsSideways(MutatorPage page)
    {
        ScrollViewer scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
        double limit = scroll.Viewport.Width + 0.5;
        var spill = page.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && c is TextBlock or Slider or ToggleSwitch or Button or NumberField or RangeSlider)
            .Select(c => (Control: c, Right: c.TranslatePoint(new Point(c.Bounds.Width, 0), page)!.Value.X))
            .Where(x => x.Right > limit)
            .Select(x => x.Control.GetType().Name + " " + (x.Control as TextBlock)?.Text)
            .ToList();
        Assert.Empty(spill);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5);
    }
}
