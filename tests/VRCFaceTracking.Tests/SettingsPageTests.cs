using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class SettingsPageTests
{
    [AvaloniaFact]
    public void OscAddressBoxCommitsWhenFocusLeaves()
    {
        App.GetService<OscRecvService>();
        IOscTarget target = App.GetService<IOscTarget>();
        (int InPort, int OutPort, string? DestinationAddress) original = (target.InPort, target.OutPort, target.DestinationAddress);
        target.InPort = 9001;
        target.OutPort = 9000;
        target.DestinationAddress = "127.0.0.1";

        var page = new SettingsPage();
        var window = new Window { Content = page, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var boxes = page.GetVisualDescendants().OfType<TextBox>().ToList();
        TextBox address = boxes.Single(box => string.Equals(box.Watermark, "127.0.0.1", StringComparison.Ordinal));
        NumericUpDown elsewhere = page.GetVisualDescendants().OfType<NumericUpDown>().First();

        try
        {
            address.Focus();
            address.Text = "19";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("127.0.0.1", target.DestinationAddress);

            address.Text = "192.0.2.10";
            elsewhere.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("192.0.2.10", target.DestinationAddress);
        }
        finally
        {
            (target.InPort, target.OutPort, target.DestinationAddress) = original;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SectionsKeepDeveloperSectionsTogetherAfterEverydaySettings(bool developer)
    {
        DeveloperSettings settings = App.GetService<DeveloperSettings>();
        bool original = settings.Enabled;
        settings.Enabled = developer;

        var page = new SettingsPage();
        var window = new Window { Content = page, Width = 900, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var expected = new List<string> { Resources.Appearance_Text, Resources.OscSectionHeader, Resources.OpenVrSectionHeader };
            if (SettingsViewModel.IsUpdateCheckAvailable)
            {
                expected.Add(Resources.UpdatesSectionHeader);
            }

            expected.Add(Resources.DeveloperSectionHeader);
            if (developer)
            {
                expected.Add(Resources.LoggingSectionHeader);
                expected.Add(Resources.AdvancedSectionHeader);
            }

            expected.Add(Resources.About_Text);

            IEnumerable<string?> headers = page.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(t => t.Classes.Contains("section-header") && t.IsEffectivelyVisible)
                .Select(t => t.Text);
            Assert.Equal(expected, headers, StringComparer.Ordinal);
        }
        finally
        {
            settings.Enabled = original;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
