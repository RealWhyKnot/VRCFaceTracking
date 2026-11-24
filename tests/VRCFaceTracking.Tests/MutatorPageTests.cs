using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Controls;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed partial class MutatorPageTests
{
    private static readonly TrackingCard[] CardOrder =
    [
        TrackingCard.Calibration,
        TrackingCard.Smoothing,
        TrackingCard.Eyes,
        TrackingCard.Mouth,
        TrackingCard.FillIns,
        TrackingCard.AutoFixes,
        TrackingCard.Advanced,
    ];

    private static readonly string[] SavedNames =
    [
        "Calibration",
        "Cheek & Dimple Fill-In",
        "Data Filter",
        "Eye Assists",
        "Mouth & Brow Assists",
        "Parameter Adjustment",
        "Tracking Glitch Fixes",
        "Unified Correctors",
    ];

    private static UnifiedTrackingMutator CreateMutator() =>
        new(NullLogger<UnifiedTrackingMutator>.Instance, App.GetService<ILocalSettingsService>(), App.GetService<IDispatcherService>());

    private static MutatorViewModel CreateViewModel(UnifiedTrackingMutator mutator) =>
        new(mutator, App.GetService<DeveloperSettings>());

    private static void AddMutations(UnifiedTrackingMutator mutator, params TrackingMutation[] extra)
    {
        foreach (TrackingMutation mutation in TrackingMutation.GetImplementingMutations().Concat(extra))
        {
            mutation.Logger = NullLogger.Instance;
            mutation.CreateProperties();
            mutator.Mutations.Add(mutation);
        }
    }

    private static (UnifiedTrackingMutator Mutator, MutatorViewModel Model) Load(params TrackingMutation[] extra)
    {
        UnifiedTrackingMutator mutator = CreateMutator();
        AddMutations(mutator, extra);
        return (mutator, CreateViewModel(mutator));
    }

    private static T Unit<T>(UnifiedTrackingMutator mutator)
        where T : TrackingMutation => mutator.Mutations.OfType<T>().Single();

    private static void Settle()
    {
        for (int frame = 0; frame < 10; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static Window Show(Control content, double width = 1280, double height = 800)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Settle();
        return window;
    }

    private static void Close(Window window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static ToggleRowViewModel Toggle(MutatorViewModel model, TrackingCard card, string key) =>
        model.FindCard(card)!.Toggles.Single(t => string.Equals(t.Metadata?.Key, key, StringComparison.Ordinal));

    private static RangeRowViewModel[] Ranges(MutatorViewModel model) =>
        [.. model.FindCard(TrackingCard.Advanced)!.Rows.OfType<RangeRowViewModel>()];

    private static void Prepare(UnifiedTrackingMutator mutator)
    {
        mutator.Enabled = true;
        Unit<Calibration>(mutator).IsActive = true;
        Unit<Filter>(mutator).IsActive = true;
        Unit<EyeAssists>(mutator).IsActive = true;
        Unit<MouthBrowAssists>(mutator).IsActive = true;
        Unit<CheekDimpleFill>(mutator).IsActive = true;
        Unit<TrackingGlitchFixes>(mutator).IsActive = true;
        Unit<ParameterAdjustment>(mutator).IsActive = true;
    }

    private sealed class UndeclaredMutation : TrackingMutation
    {
        public override string Name => "Undeclared Test Mutation";

        public override string Description => "Test only";

        public override MutationPriority Priority => MutationPriority.Postprocessor;

        [MutationProperty("Lone Toggle")]
        public bool LoneToggle { get; set; }

        public override void MutateData(ref UnifiedTrackingData data)
        {
        }
    }

    private sealed class WideMutation : TrackingMutation
    {
        public override string Name => "Wide Test Mutation";

        public override string Description => "Test only";

        public override MutationPriority Priority => MutationPriority.Postprocessor;

        [MutationProperty("Setting 01")] public float S01 { get; set; }
        [MutationProperty("Setting 02")] public float S02 { get; set; }
        [MutationProperty("Setting 03")] public float S03 { get; set; }
        [MutationProperty("Setting 04")] public float S04 { get; set; }
        [MutationProperty("Setting 05")] public float S05 { get; set; }
        [MutationProperty("Setting 06")] public float S06 { get; set; }
        [MutationProperty("Setting 07")] public float S07 { get; set; }
        [MutationProperty("Setting 08")] public float S08 { get; set; }
        [MutationProperty("Setting 09")] public float S09 { get; set; }
        [MutationProperty("Setting 10")] public float S10 { get; set; }
        [MutationProperty("Setting 11")] public float S11 { get; set; }
        [MutationProperty("Setting 12")] public float S12 { get; set; }
        [MutationProperty("Setting 13")] public float S13 { get; set; }
        [MutationProperty("Setting 14")] public float S14 { get; set; }
        [MutationProperty("Setting 15")] public float S15 { get; set; }
        [MutationProperty("Setting 16")] public float S16 { get; set; }
        [MutationProperty("Setting 17")] public float S17 { get; set; }
        [MutationProperty("Setting 18")] public float S18 { get; set; }
        [MutationProperty("Setting 19")] public float S19 { get; set; }
        [MutationProperty("Setting 20")] public float S20 { get; set; }
        [MutationProperty("Setting 21")] public float S21 { get; set; }
        [MutationProperty("Setting 22")] public float S22 { get; set; }
        [MutationProperty("Setting 23")] public float S23 { get; set; }
        [MutationProperty("Setting 24")] public float S24 { get; set; }
        [MutationProperty("Setting 25")] public float S25 { get; set; }
        [MutationProperty("Setting 26")] public float S26 { get; set; }
        [MutationProperty("Setting 27")] public float S27 { get; set; }
        [MutationProperty("Setting 28")] public float S28 { get; set; }
        [MutationProperty("Setting 29")] public float S29 { get; set; }
        [MutationProperty("Setting 30")] public float S30 { get; set; }
        [MutationProperty("Setting 31")] public float S31 { get; set; }
        [MutationProperty("Setting 32")] public float S32 { get; set; }
        [MutationProperty("Setting 33")] public float S33 { get; set; }
        [MutationProperty("Setting 34")] public float S34 { get; set; }
        [MutationProperty("Setting 35")] public float S35 { get; set; }
        [MutationProperty("Setting 36")] public float S36 { get; set; }

        public override void MutateData(ref UnifiedTrackingData data)
        {
        }
    }

    [AvaloniaFact]
    public void CardsAppearInTheDeclaredOrderWhenMutationsLoadBeforeAndAfterThePage()
    {
        UnifiedTrackingMutator early = CreateMutator();
        AddMutations(early);
        MutatorViewModel earlyModel = CreateViewModel(early);
        Assert.Equal(CardOrder, earlyModel.Cards.Select(c => c.Card));

        UnifiedTrackingMutator late = CreateMutator();
        MutatorViewModel lateModel = CreateViewModel(late);
        AddMutations(late);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CardOrder, lateModel.Cards.Select(c => c.Card));
    }

    [AvaloniaFact]
    public void MasterSwitchDisablesEveryCardAndRow()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        model.Developer.Enabled = true;
        model.RefreshStatus();
        var page = new MutatorPage(model);
        Window window = Show(page);

        Assert.All(model.Cards.SelectMany(c => c.Rows), row => Assert.True(row.IsEnabled || row.Parent is { IsChecked: false }));

        model.MasterOn = false;
        Settle();

        Assert.False(mutator.Enabled);
        ItemsControl list = page.GetVisualDescendants().OfType<ItemsControl>().Single(i => string.Equals(i.Name, "CardList", StringComparison.Ordinal));
        Assert.False(list.IsEnabled);
        Assert.All(model.Cards.SelectMany(c => c.Rows), row => Assert.False(row.IsEnabled));
        Assert.All(model.Cards.Where(c => c.Card != TrackingCard.Advanced), c => Assert.Equal(Resources.TrackingChip_Paused, c.ChipText));
        Assert.All(model.Cards, c => Assert.False(c.CanToggle));

        Assert.All(
            page.GetVisualDescendants().OfType<ToggleSwitch>().Where(t => !string.Equals(t.Name, "MasterSwitch", StringComparison.Ordinal) && t.IsEffectivelyVisible),
            t => Assert.False(t.IsEffectivelyEnabled));
        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => string.Equals(t.Text, Resources.TrackingMaster_OffDescription, StringComparison.Ordinal));

        model.MasterOn = true;
        Settle();
        Assert.True(list.IsEnabled);
        Close(window);
    }

    [AvaloniaFact]
    public void DependentsFollowTheirParentToggleAndTheirCard()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        TrackingCardViewModel eyes = model.FindCard(TrackingCard.Eyes)!;
        ToggleRowViewModel sync = Toggle(model, TrackingCard.Eyes, nameof(EyeAssists.EyelidSync));
        SliderRowViewModel strength = eyes.Rows.OfType<SliderRowViewModel>().First(r => string.Equals(r.Metadata?.Key, nameof(EyeAssists.SyncStrength), StringComparison.Ordinal));

        sync.IsChecked = true;
        Assert.True(strength.IsEnabled);

        sync.IsChecked = false;
        Assert.False(strength.IsEnabled);
        Assert.True(sync.IsEnabled);

        sync.IsChecked = true;
        eyes.IsOn = false;
        Assert.False(strength.IsEnabled);
        Assert.False(sync.IsEnabled);

        eyes.IsOn = true;
        sync.IsChecked = true;
        Assert.True(strength.IsEnabled);

        model.MasterOn = false;
        Assert.False(strength.IsEnabled);
        Assert.False(sync.IsEnabled);
    }

    [AvaloniaFact]
    public void DependentRowsRenderDisabledInTheView()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        ToggleRowViewModel sync = Toggle(model, TrackingCard.Eyes, nameof(EyeAssists.EyelidSync));
        sync.IsChecked = false;
        model.FindCard(TrackingCard.Eyes)!.IsMoreOpen = true;
        var page = new MutatorPage(model);
        Window window = Show(page);

        SliderRowViewModel strength = model.FindCard(TrackingCard.Eyes)!.Rows.OfType<SliderRowViewModel>().First(r => string.Equals(r.Metadata?.Key, nameof(EyeAssists.SyncStrength), StringComparison.Ordinal));
        Slider slider = page.GetVisualDescendants().OfType<Slider>().First(s => ReferenceEquals(s.DataContext, strength));
        Assert.False(slider.IsEffectivelyEnabled);

        sync.IsChecked = true;
        Settle();
        Assert.True(slider.IsEffectivelyEnabled);
        Close(window);
    }

    [AvaloniaTheory]
    [InlineData(0.5, 0.52, true, 0.47, 0.52)]
    [InlineData(0.5, 0.52, false, 0.5, 0.55)]
    [InlineData(0.9, 0.2, false, 0.9, 0.95)]
    [InlineData(0.9, 0.2, true, 0.15, 0.2)]
    [InlineData(-3, 9, true, -1, 1)]
    public void RangeKeepsItsMinimumGapAndNeverCrosses(double lower, double upper, bool movingUpper, double expectedLower, double expectedUpper)
    {
        (double low, double high) = RangeSlider.Constrain(lower, upper, -1, 1, 0.05, movingUpper);
        Assert.True(high - low >= 0.05 - 1e-9);
        Assert.True(low >= -1 && high <= 1);
        if (Math.Abs(lower) <= 1 && Math.Abs(upper) <= 1)
        {
            Assert.Equal(expectedLower, low, 3);
            Assert.Equal(expectedUpper, high, 3);
        }
        else
        {
            Assert.Equal(-1, low, 3);
            Assert.Equal(1, high, 3);
        }
    }

    [AvaloniaFact]
    public void RangeThumbsStopAtEachOtherAndShowBothNumbers()
    {
        var slider = new RangeSlider { Minimum = 0, Maximum = 1, Step = 0.01, Lower = 0.2, Upper = 0.8, MinimumGap = 0.05 };
        slider.MoveThumb(upper: false, 0.99);
        Assert.Equal(0.75, slider.Lower, 3);
        slider.MoveThumb(upper: true, 0.1);
        Assert.Equal(0.8, slider.Upper, 3);
        slider.Lower = 0.2;
        slider.MoveThumb(upper: true, 0.1);
        Assert.Equal(0.25, slider.Upper, 3);
    }

    [AvaloniaFact]
    public void RangeRowNumbersAreEditableFromBothEnds()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        RangeRowViewModel jaw = Ranges(model).First(r => string.Equals(r.Metadata?.Key, nameof(ParameterAdjustment.JawOpen), StringComparison.Ordinal));
        jaw.Upper = 0.6;
        jaw.Lower = 0.7;
        Assert.True(jaw.Upper - jaw.Lower >= RangeRowViewModel.Gap - 1e-6);
        Assert.True(jaw.Lower < jaw.Upper);
        Assert.False(jaw.IsDefault);
        jaw.ResetCommand.Execute(parameter: null);
        Assert.True(jaw.IsDefault);
    }

    [AvaloniaTheory]
    [InlineData("0.5", 0.5)]
    [InlineData("7", 1)]
    [InlineData("-7", 0)]
    [InlineData("0,25", 0.25)]
    [InlineData("0.333", 0.33)]
    public void TypedNumbersClampToTheRange(string typed, double expected)
    {
        var field = new NumberField { Minimum = 0, Maximum = 1, Step = 0.01, Value = 0.4, Text = typed };
        field.Commit();
        Assert.Equal(expected, field.Value, 3);
        Assert.Equal(expected.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), field.Text);
    }

    [AvaloniaTheory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1e")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("--")]
    public void TypedJunkIsRejected(string typed)
    {
        var field = new NumberField { Minimum = 0, Maximum = 1, Step = 0.01, Value = 0.4, Text = typed };
        field.Commit();
        Assert.Equal(0.4, field.Value, 3);
        Assert.Equal("0.40", field.Text);
    }

    [AvaloniaFact]
    public void HeadRangesReachMinusOne()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        RangeRowViewModel[] heads = [.. Ranges(model).Where(r => string.Equals(r.Section?.Key, "Head and neck", StringComparison.Ordinal) && r.Minimum < 0)];
        Assert.NotEmpty(heads);
        foreach (RangeRowViewModel row in heads)
        {
            Assert.Equal(-1, row.Minimum);
            Assert.Equal(1, row.Maximum);
            row.Lower = -5;
            Assert.Equal(-1, row.Lower, 3);
        }
    }

    [AvaloniaFact]
    public void CalibrationChipShowsOffLearningAndReady()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        Calibration calibration = Unit<Calibration>(mutator);
        TrackingCardViewModel card = model.FindCard(TrackingCard.Calibration)!;

        calibration.IsActive = false;
        card.Refresh();
        Assert.Equal(Resources.TrackingChip_Off, card.ChipText);
        Assert.False(card.ShowProgress);

        calibration.IsActive = true;
        foreach (CalibrationParameter shape in calibration.CalData.Shapes)
        {
            shape.Progress = 0.5f;
        }

        card.Refresh();
        Assert.Equal(ChipKind.Progress, card.ChipKind);
        Assert.True(card.ShowProgress);
        Assert.Equal(0.5, card.Progress, 2);
        Assert.Equal(Resources.TrackingChip_Learning.Replace("{0}", "50", StringComparison.Ordinal), card.ChipText);

        foreach (CalibrationParameter shape in calibration.CalData.Shapes)
        {
            shape.Progress = 1f;
        }

        card.Refresh();
        Assert.Equal(Resources.TrackingChip_Ready, card.ChipText);
        Assert.Equal(ChipKind.Success, card.ChipKind);
        Assert.False(card.ShowProgress);
    }

    [AvaloniaFact]
    public void CalibrationResetAsksBeforeRunning()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        ActionRowViewModel reset = model.FindCard(TrackingCard.Calibration)!.Rows.OfType<ActionRowViewModel>().First(a => a.NeedsConfirm);
        Assert.False(string.IsNullOrWhiteSpace(reset.ConfirmText));
    }

    [AvaloniaFact]
    public void FillInsChipFollowsEveryState()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        CheekDimpleFill fill = Unit<CheekDimpleFill>(mutator);
        TrackingCardViewModel card = model.FindCard(TrackingCard.FillIns)!;

        fill.IsActive = false;
        card.Refresh();
        Assert.Equal(Resources.TrackingChip_Off, card.ChipText);

        fill.IsActive = true;
        fill.CurrentAvatar = () => null;
        card.Refresh();
        Assert.Equal(Resources.TrackingChip_WaitingForAvatar, card.ChipText);

        fill.CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: true, ["v2/Other"]);
        card.Refresh();
        Assert.Equal(Resources.TrackingChip_PausedForVrChat, card.ChipText);

        fill.CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: false, ["v2/CheekSquint", "v2/MouthDimple"]);
        card.Refresh();
        Assert.Equal(Resources.TrackingChip_NotNeeded, card.ChipText);

        fill.CurrentAvatar = () => new AvatarParameterSnapshot(usesNativeFaceTracking: false, ["v2/JawOpen"]);
        card.Refresh();
        Assert.Equal(Resources.TrackingChip_Filling, card.ChipText);
        Assert.Equal(ChipKind.Success, card.ChipKind);
    }

    [AvaloniaFact]
    public void AutoFixRowsShowNothingUntilTheyHaveFired()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        TrackingCardViewModel card = model.FindCard(TrackingCard.AutoFixes)!;
        ToggleRowViewModel[] reporting = [.. card.Toggles.Where(t => t.ReportsTriggers)];
        Assert.NotEmpty(reporting);

        card.RefreshStatus(DateTimeOffset.UtcNow);
        foreach (ToggleRowViewModel row in reporting)
        {
            Assert.True(row.IsChecked);
            Assert.NotNull(row.Unit);
            Assert.Null(row.Unit!.LastTriggeredFor(row.Metadata!.Key));
            Assert.DoesNotContain("never", row.StateText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Resources.TrackingRow_Watching, row.StateText);
        }
    }

    [AvaloniaFact]
    public void ForceEyesClosedIsDeveloperOnly()
    {
        (UnifiedTrackingMutator mutator, MutatorViewModel model) = Load();
        Prepare(mutator);
        ToggleRowViewModel force = Toggle(model, TrackingCard.Eyes, nameof(EyeAssists.ForceEyesClosed));
        model.Developer.Enabled = false;
        Assert.False(force.IsVisible);
        model.Developer.Enabled = true;
        Assert.True(force.IsVisible);
        model.Developer.Enabled = false;
    }
}
