using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using VRCFaceTracking.Services;
using VRCFaceTracking.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace VRCFaceTracking.Tests;

public static class TestAppBuilder
{
    [ModuleInitializer]
    internal static void RedirectUserFolders()
    {
        string root = Directory.CreateTempSubdirectory("vrcft-uitests-").FullName;
        Environment.SetEnvironmentVariable(Core.Logging.LogDirectories.EnvironmentVariable, root);
        Environment.SetEnvironmentVariable(Core.Utils.DataDirectoryEnvironmentVariable, Path.Combine(root, "data"));
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        App.StartServices = false;
        App.EnableMotion = false;
        OpenVRService.Enabled = false;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
