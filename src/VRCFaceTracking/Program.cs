using System.Runtime;
using Avalonia;

namespace VRCFaceTracking;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (SteamVrCommand.TryRun(args) is { } exitCode)
        {
            Environment.ExitCode = exitCode;
            return;
        }

        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        if (OperatingSystem.IsWindows())
        {
            _ = Core.ProcessPriority.TryRaiseToAboveNormal();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
