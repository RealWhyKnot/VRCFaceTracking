using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class HomeExternalTrackerTests
{
    private static readonly TrackingOwnershipSnapshot NoOwners = new(new CapabilityOwnership(Owner: null, []), new CapabilityOwnership(Owner: null, []));
    private static readonly TrackingActivity Quiet = new(TrackingActivityState.NoData, SecondsSinceData: 0, VrChatNativeFaceTrackingActive: false);

    [Fact]
    public void DetectedTrackersShowOneNeutralLine()
    {
        var tracking = new HomeTrackingViewModel();
        Assert.False(tracking.HasExternalTracker);

        tracking.Refresh(NoOwners, Quiet, gap: null, [new DetectedExternalTracker(ExternalTrackerKind.Baballonia, "Baballonia")]);

        Assert.True(tracking.HasExternalTracker);
        Assert.Equal(TrackingText.Format(Resources.HomeExternal_Detected, "Baballonia"), tracking.ExternalTrackerText);
    }

    [Fact]
    public void SeveralTrackersAreListedTogether()
    {
        var tracking = new HomeTrackingViewModel();
        tracking.Refresh(
            NoOwners,
            Quiet,
            gap: null,
            [
                new DetectedExternalTracker(ExternalTrackerKind.Baballonia, "Baballonia"),
                new DetectedExternalTracker(ExternalTrackerKind.SteamFrame, "Steam Frame"),
            ]);

        Assert.Equal("Detected: Baballonia, Steam Frame", tracking.ExternalTrackerText);
    }

    [Fact]
    public void LineHidesAgainWhenTheTrackerStops()
    {
        var tracking = new HomeTrackingViewModel();
        tracking.Refresh(NoOwners, Quiet, gap: null, [new DetectedExternalTracker(ExternalTrackerKind.EyeTrackVr, "EyeTrackVR")]);
        tracking.Refresh(NoOwners, Quiet, gap: null, []);

        Assert.False(tracking.HasExternalTracker);
        Assert.Equal(string.Empty, tracking.ExternalTrackerText);
    }

    [AvaloniaFact]
    public void HomePageShowsTheDetectedLineOnlyWhenPresent()
    {
        var page = new MainPage();
        var window = new Window { Content = page, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)page.DataContext!;
        vm.Stop();

        try
        {
            string line = TrackingText.Format(Resources.HomeExternal_Detected, "Baballonia");
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && string.Equals(t.Text, line, StringComparison.Ordinal));

            vm.Tracking.Refresh(NoOwners, Quiet, gap: null, [new DetectedExternalTracker(ExternalTrackerKind.Baballonia, "Baballonia")]);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && string.Equals(t.Text, line, StringComparison.Ordinal));

            vm.Tracking.Refresh(NoOwners, Quiet, gap: null, []);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && string.Equals(t.Text, line, StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
