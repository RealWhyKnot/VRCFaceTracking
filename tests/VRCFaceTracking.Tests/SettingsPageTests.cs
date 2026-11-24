using Avalonia.Automation;
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
    private static (SettingsPage Page, Window Window) Open(int width, int height)
    {
        var page = new SettingsPage();
        var window = new Window { Content = page, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (page, window);
    }

    private static void Close(Window window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox Field(SettingsPage page, string name) =>
        page.GetVisualDescendants().OfType<TextBox>()
            .Single(box => string.Equals(AutomationProperties.GetName(box), name, StringComparison.Ordinal));

    private static void Commit(TextBox box, string text)
    {
        box.Focus();
        box.Text = text;
        Dispatcher.UIThread.RunJobs();
        TopLevel.GetTopLevel(box)!.FocusManager!.ClearFocus();
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<TextBlock> VisibleErrors(SettingsPage page) =>
        page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("field-error") && t.IsEffectivelyVisible);

    [AvaloniaFact]
    public void OscAddressBoxCommitsWhenFocusLeaves()
    {
        App.GetService<OscRecvService>();
        IOscTarget target = App.GetService<IOscTarget>();
        (int InPort, int OutPort, string? DestinationAddress) original = (target.InPort, target.OutPort, target.DestinationAddress);
        target.InPort = 9001;
        target.OutPort = 9000;
        target.DestinationAddress = "127.0.0.1";
        (SettingsPage page, Window window) = Open(1200, 800);

        try
        {
            TextBox address = Field(page, Resources.OscAddressLabel);
            address.Focus();
            address.Text = "192.0.2.10";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("127.0.0.1", target.DestinationAddress);

            TopLevel.GetTopLevel(page)!.FocusManager!.ClearFocus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("192.0.2.10", target.DestinationAddress);
        }
        finally
        {
            (target.InPort, target.OutPort, target.DestinationAddress) = original;
            Close(window);
        }
    }

    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData("99999999999999999999")]
    public void InvalidPortShowsInlineErrorAndKeepsValue(string text)
    {
        App.GetService<OscRecvService>();
        IOscTarget target = App.GetService<IOscTarget>();
        (int InPort, int OutPort, string? DestinationAddress) original = (target.InPort, target.OutPort, target.DestinationAddress);
        target.OutPort = 9000;
        (SettingsPage page, Window window) = Open(900, 600);

        try
        {
            TextBox box = Field(page, Resources.OscOutPortLabel);
            Commit(box, text);

            Assert.Equal(9000, target.OutPort);
            Assert.Contains("invalid", box.Classes, StringComparer.Ordinal);
            Assert.Equal(Resources.Settings_Port_Error, VisibleErrors(page).Single().Text);
        }
        finally
        {
            (target.InPort, target.OutPort, target.DestinationAddress) = original;
            Close(window);
        }
    }

    [AvaloniaTheory]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    [InlineData(" 9005 ", 9005)]
    public void ValidPortCommitsAndClearsError(string text, int expected)
    {
        App.GetService<OscRecvService>();
        IOscTarget target = App.GetService<IOscTarget>();
        (int InPort, int OutPort, string? DestinationAddress) original = (target.InPort, target.OutPort, target.DestinationAddress);
        target.OutPort = 9000;
        (SettingsPage page, Window window) = Open(900, 600);

        try
        {
            TextBox box = Field(page, Resources.OscOutPortLabel);
            Commit(box, "0");
            Assert.Contains("invalid", box.Classes, StringComparer.Ordinal);
            Commit(box, text);

            Assert.Equal(expected, target.OutPort);
            Assert.DoesNotContain("invalid", box.Classes, StringComparer.Ordinal);
            Assert.Empty(VisibleErrors(page));
        }
        finally
        {
            (target.InPort, target.OutPort, target.DestinationAddress) = original;
            Close(window);
        }
    }

    [AvaloniaFact]
    public void InvalidAddressShowsInlineError()
    {
        App.GetService<OscRecvService>();
        IOscTarget target = App.GetService<IOscTarget>();
        string? original = target.DestinationAddress;
        target.DestinationAddress = "127.0.0.1";
        (SettingsPage page, Window window) = Open(900, 600);

        try
        {
            Commit(Field(page, Resources.OscAddressLabel), "not an address");

            Assert.Equal("127.0.0.1", target.DestinationAddress);
            Assert.Equal(Resources.Settings_Address_Error, VisibleErrors(page).Single().Text);
        }
        finally
        {
            target.DestinationAddress = original;
            Close(window);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SectionsFollowEverydayThenDeveloperThenAbout(bool developer)
    {
        DeveloperSettings settings = App.GetService<DeveloperSettings>();
        bool original = settings.Enabled;
        settings.Enabled = developer;
        (SettingsPage page, Window window) = Open(900, 800);

        try
        {
            var expected = new List<string> { Resources.Appearance_Text, Resources.Settings_Connection_Title };
            if (SettingsViewModel.IsStartupVisible)
            {
                expected.Add(Resources.Settings_Startup_Title);
            }

            expected.Add(Resources.DeveloperSectionHeader);
            if (developer)
            {
                expected.Add(Resources.LoggingSectionHeader);
                expected.Add(Resources.Settings_Troubleshooting_Title);
            }

            expected.Add(Resources.About_Text);

            IEnumerable<string?> headers = page.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(t => (t.Classes.Contains("tracking-card-title") || t.Classes.Contains("tracking-section-title")) && t.IsEffectivelyVisible)
                .Select(t => t.Text);
            Assert.Equal(expected, headers, StringComparer.Ordinal);
        }
        finally
        {
            settings.Enabled = original;
            Close(window);
        }
    }

    [AvaloniaFact]
    public void CameraStreamsAreNotOnSettings()
    {
        DeveloperSettings settings = App.GetService<DeveloperSettings>();
        bool original = settings.Enabled;
        settings.Enabled = true;
        (SettingsPage page, Window window) = Open(900, 600);

        try
        {
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => string.Equals(t.Text, Resources.CameraStreamsHeader, StringComparison.Ordinal));
            Assert.Empty(page.GetVisualDescendants().OfType<Image>());
        }
        finally
        {
            settings.Enabled = original;
            Close(window);
        }
    }

    [AvaloniaFact]
    public void RepairToolsStayDisabledUntilEnabled()
    {
        DeveloperSettings settings = App.GetService<DeveloperSettings>();
        bool original = settings.Enabled;
        settings.Enabled = true;
        (SettingsPage page, Window window) = Open(900, 800);
        var vm = (SettingsViewModel)page.DataContext!;
        bool risky = vm.RiskySettings.Enabled;

        try
        {
            vm.RiskySettings.Enabled = false;
            Dispatcher.UIThread.RunJobs();
            Button reset = page.GetVisualDescendants().OfType<Button>()
                .Single(b => string.Equals(AutomationProperties.GetName(b), Resources.ResetVRCFTButton_Header, StringComparison.Ordinal));
            Assert.False(reset.IsEffectivelyEnabled);

            vm.RiskySettings.Enabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(reset.IsEffectivelyEnabled);
        }
        finally
        {
            vm.RiskySettings.Enabled = risky;
            settings.Enabled = original;
            Close(window);
        }
    }

    [AvaloniaFact]
    public void AvatarFolderResetUsesPathCombine()
    {
        string root = Path.Combine(Path.GetTempPath(), "vrcft-" + Guid.NewGuid().ToString("N"));
        string user = Path.Combine(root, "usr_1");
        string avatars = Path.Combine(user, "Avatars");
        string keep = Path.Combine(user, "Keep");
        Directory.CreateDirectory(avatars);
        Directory.CreateDirectory(keep);

        try
        {
            RiskySettingsViewModel.DeleteAvatarFolders(root);
            Assert.False(Directory.Exists(avatars));
            Assert.True(Directory.Exists(keep));
            RiskySettingsViewModel.DeleteAvatarFolders(Path.Combine(root, "missing"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
