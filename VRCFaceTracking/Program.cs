using System.Diagnostics;
using System.Runtime;
using Avalonia;

namespace VRCFaceTracking;

internal class Program
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
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            }
            catch (Exception)
            {
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
