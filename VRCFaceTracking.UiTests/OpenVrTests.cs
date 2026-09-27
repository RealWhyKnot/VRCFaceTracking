using System.Diagnostics;
using Avalonia.Headless.XUnit;
using VRCFaceTracking.Services;

namespace VRCFaceTracking.UiTests;

public class OpenVrTests
{
    [AvaloniaFact]
    public void TestProcessNeverInitializesOpenVr()
    {
        Assert.False(App.GetService<OpenVRService>().Initialize());
        Assert.DoesNotContain(Process.GetCurrentProcess().Modules.Cast<ProcessModule>(),
            module => module.ModuleName.StartsWith("openvr_api", StringComparison.OrdinalIgnoreCase));
    }
}
