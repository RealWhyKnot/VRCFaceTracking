using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Services;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.UiTests;

public class SettingsPageTests
{
    [AvaloniaFact]
    public void OscAddressBox_CommitsWhenFocusLeaves()
    {
        App.GetService<OscRecvService>();
        var target = App.GetService<IOscTarget>();
        var original = (target.InPort, target.OutPort, target.DestinationAddress);
        target.InPort = 9001;
        target.OutPort = 9000;
        target.DestinationAddress = "127.0.0.1";

        var page = new SettingsPage();
        var window = new Window { Content = page, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var boxes = page.GetVisualDescendants().OfType<TextBox>().ToList();
        var address = boxes.Single(box => box.Watermark == "127.0.0.1");
        var elsewhere = page.GetVisualDescendants().OfType<NumericUpDown>().First();

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
}
