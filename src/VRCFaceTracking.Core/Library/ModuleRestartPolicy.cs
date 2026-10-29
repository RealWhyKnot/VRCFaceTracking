namespace VRCFaceTracking.Core.Library;

public sealed class ModuleRestartPolicy
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    ];

    private readonly Dictionary<string, List<DateTime>> _attempts = [];
    private readonly Lock _lock = new();

    public static bool IsRestartableExitCode(int exitCode) => exitCode switch
    {
        ModuleProcessExitCodes.OK => false,
        ModuleProcessExitCodes.InvalidArgs => false,
        ModuleProcessExitCodes.ModuleLoadFailed => false,
        ModuleProcessExitCodes.ParentExited => false,
        _ => true,
    };

    public TimeSpan? NextRestartDelay(string moduleKey, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_attempts.TryGetValue(moduleKey, out List<DateTime>? history))
            {
                history = [];
                _attempts[moduleKey] = history;
            }

            history.RemoveAll(t => nowUtc - t > Window);
            if (history.Count >= MaxAttempts)
            {
                return null;
            }

            TimeSpan delay = Delays[history.Count];
            history.Add(nowUtc);
            return delay;
        }
    }

    public int AttemptNumber(string moduleKey)
    {
        lock (_lock)
        {
            return _attempts.TryGetValue(moduleKey, out List<DateTime>? history) ? history.Count : 0;
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
