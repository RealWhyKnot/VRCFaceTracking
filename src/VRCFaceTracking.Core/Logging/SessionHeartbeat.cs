using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public sealed partial class SessionHeartbeat : IDisposable
{
    public const string FileName = "vrcft_session.heartbeat";
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _interval;
    private readonly int _processId;
    private readonly Lock _gate = new();
    private Timer? _timer;
    private bool _stopped;

    public SessionHeartbeat(ILogger logger, string directory, TimeSpan? interval = null, Func<DateTimeOffset>? clock = null, int? processId = null)
    {
        _logger = logger;
        _interval = interval ?? DefaultInterval;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _processId = processId ?? Environment.ProcessId;
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    public DateTimeOffset? PreviousSessionLastHeartbeat { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_timer != null)
            {
                return;
            }

            DetectUncleanShutdown();
            _stopped = false;
            Write();
            _timer = new Timer(_ => Write(), state: null, _interval, _interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _timer?.Dispose();
            _timer = null;
            try
            {
                File.Delete(FilePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogHeartbeatCleanupFailed(_logger, e.Message);
            }
        }
    }

    public void Dispose() => Stop();

    internal void Write()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(FilePath, _clock().ToString("O", CultureInfo.InvariantCulture) + "\n" + _processId.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogHeartbeatWriteFailed(_logger, e.Message);
            }
        }
    }

    private void DetectUncleanShutdown()
    {
        string[] lines;
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            lines = File.ReadAllLines(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogHeartbeatReadFailed(_logger, e.Message);
            return;
        }

        if (lines.Length > 1 && int.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) && pid != _processId && IsRunning(pid))
        {
            return;
        }

        if (lines.Length > 0 && DateTimeOffset.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset last))
        {
            PreviousSessionLastHeartbeat = last;
            LogPreviousSessionEndedUnexpectedly(_logger, last);
        }
        else
        {
            LogPreviousSessionEndedUnexpectedlyUnknown(_logger);
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The previous session ended unexpectedly. Last heartbeat at {LastHeartbeat:u}")]
    private static partial void LogPreviousSessionEndedUnexpectedly(ILogger logger, DateTimeOffset lastHeartbeat);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The previous session ended unexpectedly. Its last heartbeat time is unknown")]
    private static partial void LogPreviousSessionEndedUnexpectedlyUnknown(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Session heartbeat write failed: {Message}")]
    private static partial void LogHeartbeatWriteFailed(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Session heartbeat read failed: {Message}")]
    private static partial void LogHeartbeatReadFailed(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Session heartbeat cleanup failed: {Message}")]
    private static partial void LogHeartbeatCleanupFailed(ILogger logger, string message);
}
