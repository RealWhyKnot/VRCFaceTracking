using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public static class CrashHandlers
{
    public static void Install(ILogger logger, Action flush)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception in process (terminating={Terminating})", e.IsTerminating);
            flush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
            flush();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => flush();
    }
}
