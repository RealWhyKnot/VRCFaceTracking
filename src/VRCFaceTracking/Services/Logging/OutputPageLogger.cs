using System.Collections.Concurrent;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Logging;
using VRCFaceTracking.Helpers;
using VRCFaceTracking.Models;

namespace VRCFaceTracking.Services.Logging;

internal sealed class OutputPageLogger : ILogger
{
    public static readonly BoundedObservableCollection<LogLine> AllLogs = new(10000);

    internal static readonly ConcurrentQueue<LogLine> Pending = new();
    private static DispatcherTimer? FlushTimer;
    private static int TimerStarted;

    private readonly string _categoryName;
    private readonly LogLevelGate _gate;

    public OutputPageLogger(string categoryName, LogLevelGate gate)
    {
        _categoryName = categoryName;
        _gate = gate;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information || _gate.Verbose;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        LogLine line = string.Equals(_categoryName, "\0VRCFT\0", StringComparison.Ordinal)
            ? new LogLine($"{formatter(state, exception)}", logLevel)
            : new LogLine($"[{_categoryName}] {logLevel}: {formatter(state, exception)}", logLevel);

        Pending.Enqueue(line);
        EnsureFlushTimer();
    }

    private static void EnsureFlushTimer()
    {
        if (Interlocked.CompareExchange(ref TimerStarted, 1, 0) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            FlushTimer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Background,
                Flush);
            FlushTimer.Start();
        });
    }

    private static void Flush(object? sender, EventArgs e)
    {
        while (Pending.TryDequeue(out LogLine? line))
        {
            AllLogs.Add(line);
        }

        if (Pending.IsEmpty)
        {
            FlushTimer?.Stop();
            Interlocked.Exchange(ref TimerStarted, 0);
        }
    }
}
