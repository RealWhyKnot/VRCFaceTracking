using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Library;

public sealed class ModuleProcessWatcher
{
    public const int StderrTailLines = 20;

    private readonly Queue<string> _stderrTail = new();
    private readonly object _lock = new();

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

            moduleLogger.LogWarning("[{Module} stderr] {Line}", moduleName, e.Data);
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
                code = ModuleProcessExitCodes.EXCEPTION_CRASH;
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
}
