using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Data.Mutation;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class AvatarGapDisplayTests
{
    private static AvatarGapReport Report(int covered = 0, int filled = 0, int missing = 0, int native = 0)
    {
        List<AvatarGapEntry> entries = [];
        for (int i = 0; i < covered; i++)
        {
            entries.Add(new AvatarGapEntry((UnifiedExpressions)i, AvatarGapStatus.Covered, FillSource: null));
        }

        for (int i = 0; i < filled; i++)
        {
            entries.Add(new AvatarGapEntry((UnifiedExpressions)(i % 50), AvatarGapStatus.Filled, "EyeSquint"));
        }

        for (int i = 0; i < missing; i++)
        {
            entries.Add(new AvatarGapEntry((UnifiedExpressions)(i % 50), AvatarGapStatus.Missing, FillSource: null));
        }

        for (int i = 0; i < native; i++)
        {
            entries.Add(new AvatarGapEntry((UnifiedExpressions)i, AvatarGapStatus.NativeSkip, FillSource: null));
        }

        return new AvatarGapReport(entries);
    }

    private static void Settle()
    {
        for (int frame = 0; frame < 10; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static TextBlock? Find(Visual root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => string.Equals(t.Text, text, StringComparison.Ordinal) && t.IsEffectivelyVisible);

    private static TrackingOwnershipSnapshot NoOwners() => new(new CapabilityOwnership(Owner: null, Claimants: []), new CapabilityOwnership(Owner: null, Claimants: []));

    private static TrackingActivity Active() => new(TrackingActivityState.Active, SecondsSinceData: 0, VrChatNativeFaceTrackingActive: false);

    public static TheoryData<int, int, int, int, string?> Wording => new()
    {
        { 0, 0, 0, 0, null },
        { 5, 0, 0, 0, "All tracked shapes supported" },
        { 5, 0, 1, 0, "1 tracked shape missing on avatar" },
        { 5, 0, 3, 0, "3 tracked shapes missing on avatar" },
        { 5, 1, 0, 0, "1 tracked shape missing on avatar (1 filled)" },
        { 5, 2, 1, 0, "3 tracked shapes missing on avatar (2 filled)" },
        { 0, 0, 0, 4, null },
    };

    [Theory]
    [MemberData(nameof(Wording))]
    public void GapLineUsesPlainEnglish(int covered, int filled, int missing, int native, string? expected)
    {
        string? text = AvatarGapText.Describe(Report(covered, filled, missing, native));

        Assert.Equal(expected, text);
    }

    [Fact]
    public void NoReportMeansNoLine() => Assert.Null(AvatarGapText.Describe(report: null));

    [Theory]
    [InlineData(2, 2, "2 tracked shapes missing on avatar (2 filled)")]
    [InlineData(1, 1, "1 tracked shape missing on avatar (1 filled)")]
    [InlineData(3, 0, "3 tracked shapes missing on avatar")]
    public void GapLineReadsAsASentence(int missing, int filled, string expected) =>
        Assert.Equal(expected, AvatarGapText.Describe(Report(covered: 4, filled: filled, missing: missing - filled)));

    [Fact]
    public void HomeTrackingShowsAndClearsTheGapLine()
    {
        var tracking = new HomeTrackingViewModel();
        var changes = new List<string?>();
        tracking.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.False(tracking.HasGap);

        tracking.Refresh(NoOwners(), Active(), Report(covered: 4, missing: 2));
        Assert.True(tracking.HasGap);
        Assert.Equal("2 tracked shapes missing on avatar", tracking.GapText);

        changes.Clear();
        tracking.Refresh(NoOwners(), Active(), Report(covered: 4, missing: 2));
        Assert.DoesNotContain(changes, c => string.Equals(c, nameof(HomeTrackingViewModel.GapText), StringComparison.Ordinal));

        tracking.Refresh(NoOwners(), Active(), gap: null);
        Assert.False(tracking.HasGap);
        Assert.Equal(string.Empty, tracking.GapText);
    }

    [AvaloniaFact]
    public void FillInCardShowsTheGapOnlyWhileItIsOn()
    {
        UnifiedTrackingMutator mutator = new(NullLogger<UnifiedTrackingMutator>.Instance, App.GetService<ILocalSettingsService>(), App.GetService<IDispatcherService>());
        foreach (TrackingMutation mutation in TrackingMutation.GetImplementingMutations())
        {
            mutation.Logger = NullLogger.Instance;
            mutation.CreateProperties();
            mutator.Mutations.Add(mutation);
        }

        OscQueryService osc = App.GetService<OscQueryService>();
        var model = new MutatorViewModel(mutator, App.GetService<DeveloperSettings>(), osc);
        TrackingCardViewModel card = model.FindCard(TrackingCard.FillIns)!;
        TrackingCardViewModel eyes = model.FindCard(TrackingCard.Eyes)!;
        mutator.Mutations.OfType<SignalFillIn>().Single().IsActive = true;

        try
        {
            osc.GapReport = null;
            card.Refresh();
            Assert.False(card.HasGap);

            osc.GapReport = Report(covered: 3, filled: 2, missing: 0);
            card.Refresh();
            Assert.True(card.HasGap);
            Assert.Equal("2 tracked shapes missing on avatar (2 filled)", card.GapText);
            Assert.False(eyes.HasGap);
            Assert.False(card.ShowDescription);
            Assert.True(eyes.ShowDescription);

            osc.GapReport = Report(covered: 6);
            card.RefreshStatus();
            Assert.Equal(Resources.AvatarGap_AllSupported, card.GapText);

            card.IsOn = false;
            Assert.False(card.HasGap);
        }
        finally
        {
            osc.GapReport = null;
        }
    }

    [AvaloniaTheory]
    [InlineData(1280, 800)]
    [InlineData(900, 600)]
    public void HomeCardKeepsTheGapLineInsideTheWindow(int width, int height)
    {
        var page = new MainPage();
        var window = new Window { Content = page, Width = width, Height = height };
        window.Show();
        Settle();
        var vm = (MainViewModel)page.DataContext!;
        vm.Stop();

        try
        {
            vm.Tracking.Refresh(NoOwners(), Active(), Report(covered: 40, filled: 100, missing: 899));
            Settle();

            TextBlock line = Find(page, vm.Tracking.GapText)!;
            Assert.NotNull(line);
            Rect box = new Rect(line.Bounds.Size).TransformToAABB(line.TransformToVisual(window)!.Value);
            Assert.True(box.Left >= 0 && box.Right <= width, $"line spans {box.Left}..{box.Right} in {width}");
            Assert.True(box.Height > 0);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
