using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class HomePageTests
{
    private static CapabilityOwnership Owned(string? owner, params string[] claimants) => new(owner, claimants);

    private static (MainPage Page, Window Window, MainViewModel Vm) Open(int width, int height)
    {
        var page = new MainPage();
        var window = new Window { Content = page, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)page.DataContext!;
        vm.Stop();
        return (page, window, vm);
    }

    private static void Close(Window window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void SingleOwnerShowsNoNote()
    {
        var row = new OwnerRowViewModel(Resources.HomeOwner_Eyes);
        row.Update(Owned("Module A", "Module A"));

        Assert.Equal("Module A", row.Owner);
        Assert.True(row.HasOwner);
        Assert.False(row.HasNote);
    }

    [Fact]
    public void TwoModulesShowNeutralNote()
    {
        var row = new OwnerRowViewModel(Resources.HomeOwner_Eyes);
        row.Update(Owned("Module A", "Module A", "Module B"));

        Assert.Equal("Module A", row.Owner);
        Assert.True(row.HasNote);
        Assert.Equal(TrackingText.Format(Resources.HomeOwner_AlsoFrom, "Module B"), row.Note);
    }

    [Fact]
    public void NoOwnerShowsPlaceholder()
    {
        var row = new OwnerRowViewModel(Resources.HomeOwner_Mouth);
        row.Update(Owned("Module A"));
        row.Update(Owned(owner: null));

        Assert.Equal(Resources.HomeOwner_None, row.Owner);
        Assert.False(row.HasOwner);
    }

    [Theory]
    [InlineData(TrackingActivityState.Active, false, "HomeActivity_Tracking", true)]
    [InlineData(TrackingActivityState.Idle, false, "HomeActivity_NoNewData", false)]
    [InlineData(TrackingActivityState.NoData, false, "HomeActivity_NoData", false)]
    [InlineData(TrackingActivityState.Active, true, "HomeActivity_PausedForVrChat", false)]
    public void ActivityChipMatchesState(TrackingActivityState state, bool native, string key, bool success)
    {
        (string text, ChipKind chip) = HomeTrackingViewModel.Describe(new TrackingActivity(state, SecondsSinceData: 0, native));

        string expected = (string)typeof(Resources).GetProperty(key)!.GetValue(obj: null)!;
        Assert.Equal(expected, text);
        Assert.Equal(success ? ChipKind.Success : ChipKind.Neutral, chip);
    }

    [Fact]
    public void ActivityStringsAreLocalizedPlainText()
    {
        Assert.Equal("Tracking", Resources.HomeActivity_Tracking);
        Assert.Equal("No new data", Resources.HomeActivity_NoNewData);
        Assert.Equal("No tracking data", Resources.HomeActivity_NoData);
        Assert.Equal("Paused for VRChat face tracking", Resources.HomeActivity_PausedForVrChat);
    }

    [AvaloniaFact]
    public void HomePageShowsOwnersWithoutErrorStyling()
    {
        (MainPage page, Window window, MainViewModel vm) = Open(900, 600);

        try
        {
            vm.Tracking.Refresh(
                new TrackingOwnershipSnapshot(Owned("Module A", "Module A", "Module B"), Owned("Module A", "Module A")),
                new TrackingActivity(TrackingActivityState.Active, SecondsSinceData: 0, VrChatNativeFaceTrackingActive: false));
            Dispatcher.UIThread.RunJobs();

            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Module A", texts, StringComparer.Ordinal);
            Assert.Contains(TrackingText.Format(Resources.HomeOwner_AlsoFrom, "Module B"), texts, StringComparer.Ordinal);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<InfoBar>(), bar => bar.IsEffectivelyVisible && string.Equals(bar.Message, Resources.HomeOwner_AlsoFrom, StringComparison.Ordinal));
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("error") || t.Classes.Contains("warning"));
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaFact]
    public void HomePageHidesCameraCardUntilFramesArrive()
    {
        (MainPage page, Window window, MainViewModel vm) = Open(900, 600);

        try
        {
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && string.Equals(t.Text, Resources.CameraStreamsHeader, StringComparison.Ordinal));

            vm.Cameras.Refresh([CameraTestData.Snap("eye"), CameraTestData.Snap("lip")]);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && string.Equals(t.Text, Resources.CameraStreamsHeader, StringComparison.Ordinal));
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaFact]
    public void LongModuleNamesStayInsideTheWindow()
    {
        (MainPage page, Window window, MainViewModel vm) = Open(900, 600);

        try
        {
            string longName = new('W', 120);
            vm.Tracking.Refresh(
                new TrackingOwnershipSnapshot(Owned(longName, longName, longName + "2"), Owned(longName, longName)),
                new TrackingActivity(TrackingActivityState.Idle, SecondsSinceData: 3, VrChatNativeFaceTrackingActive: false));
            Dispatcher.UIThread.RunJobs();

            foreach (TextBlock text in page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible))
            {
                Point origin = text.TranslatePoint(new Point(0, 0), page) ?? default;
                Assert.True(origin.X + text.Bounds.Width <= page.Bounds.Width + 1, text.Text);
            }
        }
        finally
        {
            Close(window);
        }
    }
}
