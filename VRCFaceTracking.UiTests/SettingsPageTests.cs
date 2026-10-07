using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Views;

namespace VRCFaceTracking.UiTests;

public class SettingsPageTests
{
    [AvaloniaFact]
    public void OscAddressBox_CommitsWhenFocusLeaves()
    {
        var page = new SettingsPage();
        var window = new Window { Content = page, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var target = App.GetService<IOscTarget>();
        var original = target.DestinationAddress;
        var boxes = page.GetVisualDescendants().OfType<TextBox>().ToList();
        var address = boxes.Single(box => box.Watermark == "127.0.0.1");
        var elsewhere = page.GetVisualDescendants().OfType<NumericUpDown>().First();

        try
        {
            address.Focus();
            address.Text = "19";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(original, target.DestinationAddress);

            address.Text = "192.0.2.10";
            elsewhere.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("192.0.2.10", target.DestinationAddress);
        }
        finally
        {
            target.DestinationAddress = original;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
