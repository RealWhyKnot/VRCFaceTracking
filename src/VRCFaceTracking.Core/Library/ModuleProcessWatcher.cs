using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Library;

public sealed partial class ModuleProcessWatcher
{
    public const int StderrTailLines = 20;

    private readonly Queue<string> _stderrTail = new();
    private readonly Lock _lock = new();

    public ModuleProcessWatcher(Process process, string moduleName, ILogger moduleLogger, Action<int> onExited)
    {
        process.EnableRaisingEvents = true;
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (_lock)
            {
                _stderrTail.Enqueue(e.Data);
                while (_stderrTail.Count > StderrTailLines)
                {
                    _stderrTail.Dequeue();
                }
            }

            LogStderr(moduleLogger, moduleName, e.Data);
        };
        process.Exited += (_, _) =>
        {
            int code;
            try
            {
                code = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = ModuleProcessExitCodes.ExceptionCrash;
            }

            onExited(code);
        };
        process.BeginErrorReadLine();
    }

    public string StderrTail
    {
        get
        {
            lock (_lock)
            {
                return string.Join(Environment.NewLine, _stderrTail);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{Module} stderr] {Line}")]
    private static partial void LogStderr(ILogger logger, string module, string line);
}
