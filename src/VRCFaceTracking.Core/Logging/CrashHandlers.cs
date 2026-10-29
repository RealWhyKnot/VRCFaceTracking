using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public static partial class CrashHandlers
{
    public static void ReportUnhandled(ILogger logger, UnhandledExceptionEventArgs args, Action flush)
    {
        LogUnhandledExceptionProcessTerminating(logger, args.ExceptionObject as Exception, args.IsTerminating);
        flush();
    }

    public static void ReportUnobserved(ILogger logger, UnobservedTaskExceptionEventArgs args, Action flush)
    {
        LogUnobservedTaskException(logger, args.Exception);
        args.SetObserved();
        flush();
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception in process (terminating={Terminating})")]
    private static partial void LogUnhandledExceptionProcessTerminating(ILogger logger, Exception? exception, bool terminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}
