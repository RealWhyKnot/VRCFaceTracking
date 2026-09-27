using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using VRCFaceTracking;
using VRCFaceTracking.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace VRCFaceTracking.UiTests;

public static class TestAppBuilder
{
    [ModuleInitializer]
    internal static void RedirectUserFolders()
    {
        var root = Directory.CreateTempSubdirectory("vrcft-uitests-").FullName;
        Environment.SetEnvironmentVariable(Core.Utils.LogDirectoryEnvironmentVariable, root);
        Environment.SetEnvironmentVariable(Core.Utils.DataDirectoryEnvironmentVariable, Path.Combine(root, "data"));
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        App.StartServices = false;
        App.EnableMotion = false;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
