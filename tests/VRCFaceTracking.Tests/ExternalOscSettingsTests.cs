using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Strings;
using VRCFaceTracking.ViewModels;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.Tests;

public sealed class ExternalOscSettingsTests
{
    private static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static (SettingsPage Page, Window Window, SettingsViewModel Vm) Open(int width, int height)
    {
        var page = new SettingsPage();
        var window = new Window { Content = page, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (SettingsViewModel)page.DataContext!;
        vm.ReloadOscFields();
        Dispatcher.UIThread.RunJobs();
        return (page, window, vm);
    }

    private static void Close(Window window)
    {
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static ToggleSwitch Toggle(SettingsPage page) =>
        page.GetVisualDescendants().OfType<ToggleSwitch>()
            .Single(t => string.Equals(AutomationProperties.GetName(t), Resources.ExternalOsc_Label, StringComparison.Ordinal));

    private static List<TextBox> PortBoxes(SettingsPage page) =>
        [.. page.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.IsEffectivelyVisible && string.Equals(AutomationProperties.GetName(t), Resources.ExternalOsc_PortLabel, StringComparison.Ordinal))];

    private static List<TextBlock> VisibleErrors(SettingsPage page) =>
        [.. page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("field-error") && t.IsEffectivelyVisible)];

    private static void Commit(TextBox box, string text)
    {
        box.Focus();
        box.Text = text;
        Dispatcher.UIThread.RunJobs();
        TopLevel.GetTopLevel(box)!.FocusManager!.ClearFocus();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Restore(ExternalOscInputService service, int port)
    {
        service.Settings.Enabled = false;
        service.Settings.Port = port;
        service.Apply();
    }

    [AvaloniaFact]
    public void ExternalInputIsOffByDefaultAndHidesItsPort()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        Assert.False(service.Settings.Enabled);
        Assert.Equal(8888, service.Settings.Port);
        Assert.Equal(ExternalOscState.Disabled, service.Status.State);

        (SettingsPage page, Window window, SettingsViewModel vm) = Open(1280, 800);
        try
        {
            Assert.False(Toggle(page).IsChecked);
            Assert.Empty(PortBoxes(page));
            Assert.False(vm.HasExternalOscStatus);
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaFact]
    public void TogglingOnBindsListensAndShowsThePort()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        int original = service.Settings.Port;
        service.Settings.Port = FreePort();
        (SettingsPage page, Window window, SettingsViewModel vm) = Open(1280, 800);

        try
        {
            Toggle(page).IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(service.Settings.Enabled);
            Assert.Equal(ExternalOscState.Listening, service.Status.State);
            Assert.Single(PortBoxes(page));
            Assert.Contains(service.Settings.Port.ToString(CultureInfo.InvariantCulture), vm.ExternalOscStatusText, StringComparison.Ordinal);

            Toggle(page).IsChecked = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ExternalOscState.Disabled, service.Status.State);
            Assert.Empty(PortBoxes(page));
            Assert.False(vm.HasExternalOscStatus);
        }
        finally
        {
            Restore(service, original);
            Close(window);
        }
    }

    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("")]
    public void InvalidExternalPortShowsInlineError(string text)
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        int original = service.Settings.Port;
        int good = FreePort();
        service.Settings.Port = good;
        (SettingsPage page, Window window, _) = Open(900, 600);

        try
        {
            Toggle(page).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            TextBox box = PortBoxes(page).Single();
            Commit(box, text);

            Assert.Equal(good, service.Settings.Port);
            Assert.Contains("invalid", box.Classes, StringComparer.Ordinal);
            Assert.Equal(Resources.Settings_Port_Error, VisibleErrors(page).Single().Text);
        }
        finally
        {
            Restore(service, original);
            Close(window);
        }
    }

    [AvaloniaFact]
    public void PortUsedByVrchatIsRejectedInline()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        IOscTarget target = App.GetService<IOscTarget>();
        int original = service.Settings.Port;
        int originalIn = target.InPort;
        target.InPort = 9001;
        service.Settings.Port = FreePort();
        (SettingsPage page, Window window, _) = Open(900, 600);

        try
        {
            Toggle(page).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            int before = service.Settings.Port;
            Commit(PortBoxes(page).Single(), "9001");

            Assert.Equal(before, service.Settings.Port);
            Assert.Equal(Resources.ExternalOsc_PortConflict, VisibleErrors(page).Single().Text);
        }
        finally
        {
            target.InPort = originalIn;
            Restore(service, original);
            Close(window);
        }
    }

    private static void Enable(ExternalOscInputService service, int port)
    {
        service.Settings.Port = port;
        service.Settings.Enabled = true;
        service.Apply();
    }

    [AvaloniaFact]
    public void VrchatPortFieldsRejectTheExternalPort()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        IOscTarget target = App.GetService<IOscTarget>();
        int original = service.Settings.Port;
        int originalIn = target.InPort;
        int originalOut = target.OutPort;
        int port = FreePort();
        Enable(service, port);
        (_, Window window, SettingsViewModel vm) = Open(900, 600);

        try
        {
            string text = port.ToString(CultureInfo.CurrentCulture);
            vm.InPortField.Text = text;
            vm.OutPortField.Text = text;

            Assert.Equal(Resources.Settings_Port_ExternalConflict, vm.InPortField.Error);
            Assert.Equal(Resources.Settings_Port_ExternalConflict, vm.OutPortField.Error);
            Assert.Equal(originalIn, target.InPort);
            Assert.Equal(originalOut, target.OutPort);
            Assert.Equal(ExternalOscState.Listening, service.Status.State);
        }
        finally
        {
            Restore(service, original);
            vm.ReloadOscFields();
            Close(window);
        }
    }

    [AvaloniaFact]
    public void VrchatPortMovedOntoTheExternalPortShowsTheConflict()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        IOscTarget target = App.GetService<IOscTarget>();
        int original = service.Settings.Port;
        int originalOut = target.OutPort;
        int port = FreePort();
        Enable(service, port);
        (_, Window window, SettingsViewModel vm) = Open(900, 600);

        try
        {
            target.OutPort = port;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ExternalOscState.Error, service.Status.State);
            Assert.Equal(Resources.ExternalOsc_PortConflict, vm.ExternalOscStatusText);

            target.OutPort = originalOut;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ExternalOscState.Listening, service.Status.State);
            Assert.Equal(TrackingText.Format(Resources.ExternalOsc_Listening, port), vm.ExternalOscStatusText);
        }
        finally
        {
            target.OutPort = originalOut;
            Restore(service, original);
            Close(window);
        }
    }

    [AvaloniaFact]
    public void PortHeldByAnotherAppSaysSo()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        int original = service.Settings.Port;
        using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ExclusiveAddressUse = true };
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)holder.LocalEndPoint!).Port;
        Enable(service, port);
        (_, Window window, SettingsViewModel vm) = Open(900, 600);

        try
        {
            Assert.Equal(ExternalOscState.Error, service.Status.State);
            Assert.Equal(Resources.ExternalOsc_PortInUse, vm.ExternalOscStatusText);
        }
        finally
        {
            Restore(service, original);
            Close(window);
        }
    }

    [AvaloniaFact]
    public void ValidExternalPortCommitsAndRebinds()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        int original = service.Settings.Port;
        service.Settings.Port = FreePort();
        (SettingsPage page, Window window, _) = Open(900, 600);

        try
        {
            Toggle(page).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            int next = FreePort();
            Commit(PortBoxes(page).Single(), next.ToString(CultureInfo.InvariantCulture));

            Assert.Equal(next, service.Settings.Port);
            Assert.Equal(ExternalOscState.Listening, service.Status.State);
            Assert.Empty(VisibleErrors(page));
        }
        finally
        {
            Restore(service, original);
            Close(window);
        }
    }

    [AvaloniaFact]
    public void ConnectionCardFitsAt900By600()
    {
        ExternalOscInputService service = App.GetService<ExternalOscInputService>();
        int original = service.Settings.Port;
        service.Settings.Port = FreePort();
        (SettingsPage page, Window window, _) = Open(900, 600);

        try
        {
            Toggle(page).IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            foreach (Control control in page.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is TextBlock or TextBox or ToggleSwitch))
            {
                Point origin = control.TranslatePoint(new Point(0, 0), page) ?? default;
                Assert.True(origin.X >= 0 && origin.X + control.Bounds.Width <= page.Bounds.Width + 1, control.GetType().Name);
            }
        }
        finally
        {
            Restore(service, original);
            Close(window);
        }
    }
}
