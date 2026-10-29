namespace VRCFaceTracking.Core.Library;

public class ModuleRestartPolicy
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    };

    private readonly Dictionary<string, List<DateTime>> _attempts = new();
    private readonly object _lock = new();

    public static bool IsRestartableExitCode(int exitCode) => exitCode switch
    {
        ModuleProcessExitCodes.OK => false,
        ModuleProcessExitCodes.INVALID_ARGS => false,
        ModuleProcessExitCodes.MODULE_LOAD_FAILED => false,
        ModuleProcessExitCodes.PARENT_EXITED => false,
        _ => true,
    };

    public TimeSpan? NextRestartDelay(string moduleKey, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_attempts.TryGetValue(moduleKey, out var history))
            {
                history = new List<DateTime>();
                _attempts[moduleKey] = history;
            }

            history.RemoveAll(t => nowUtc - t > Window);
            if (history.Count >= MaxAttempts)
            {
                return null;
            }

            var delay = Delays[history.Count];
            history.Add(nowUtc);
            return delay;
        }
    }

    public int AttemptNumber(string moduleKey)
    {
        lock (_lock)
        {
            return _attempts.TryGetValue(moduleKey, out var history) ? history.Count : 0;
        }
    }

    public void ResetAll()
    {
        lock (_lock)
        {
            _attempts.Clear();
        }
    }
}
