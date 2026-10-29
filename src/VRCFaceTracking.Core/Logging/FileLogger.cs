using System.Text;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Logging;

public sealed class FileLogger : ILogger
{
    public const string PassthroughCategory = "\0VRCFT\0";

    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;
    private readonly LogLevelGate _gate;

    public FileLogger(string categoryName, FileLoggerProvider provider, LogLevelGate gate)
    {
        _categoryName = categoryName;
        _provider = provider;
        _gate = gate;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => _gate.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        _provider.Write(logLevel, FormatLine(_categoryName, logLevel, formatter(state, exception), exception, DateTime.Now));
    }

    public static string FormatLine(string category, LogLevel level, string message, Exception? exception, DateTime now)
    {
        var sb = new StringBuilder(message.Length + 64);
        sb.Append(now.ToString("HH:mm:ss.fff")).Append(' ');
        if (category == PassthroughCategory)
        {
            sb.Append(message);
        }
        else
        {
            sb.Append('[').Append(Abbreviate(level)).Append("] [").Append(category).Append("] ").Append(message);
        }

        if (exception != null)
        {
            sb.AppendLine().Append(exception);
        }

        sb.AppendLine();
        return sb.ToString();
    }

    public static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };
}
