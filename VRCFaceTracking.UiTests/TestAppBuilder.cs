using Avalonia;
using Avalonia.Headless;
using VRCFaceTracking;
using VRCFaceTracking.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace VRCFaceTracking.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.StartServices = false;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
