using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VRCFaceTracking.Core.Logging;

[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly LogLevelGate _gate;
    private StreamWriter? _writer;
    private readonly Timer? _flushTimer;
    private readonly Lock _lock = new();

    public string? FilePath
    {
        get;
    }

    public bool IsOpen => _writer != null;

    internal TimeProvider TimeProvider
    {
        get;
    }

    public FileLoggerProvider(string directory, string fileName, string header, LogLevelGate gate, TimeProvider timeProvider)
    {
        _gate = gate;
        TimeProvider = timeProvider;
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            }
            catch (IOException)
            {
                path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fileName)}_{Environment.ProcessId}.log");
                stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            }

            _writer = new StreamWriter(stream) { AutoFlush = false };
            _writer.Write(header);
            _writer.Flush();
            FilePath = path;
            _flushTimer = new Timer(_ => Flush(), state: null, 1000, 1000);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Trace.TraceWarning($"Log file could not be opened: {e.Message}");
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        _writer == null ? NullLogger.Instance : _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this, _gate));

    public void WriteRaw(string line)
    {
        lock (_lock)
        {
            try
            {
                _writer?.WriteLine(line);
                _writer?.Flush();
            }
            catch (IOException e)
            {
                Trace.TraceWarning($"Log file write failed: {e.Message}");
            }
        }
    }

    internal void Write(LogLevel level, string line)
    {
        lock (_lock)
        {
            try
            {
                _writer?.Write(line);
                if (level >= LogLevel.Warning)
                {
                    _writer?.Flush();
                }
            }
            catch (IOException e)
            {
                Trace.TraceWarning($"Log file write failed: {e.Message}");
            }
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            try
            {
                _writer?.Flush();
            }
            catch (IOException e)
            {
                Trace.TraceWarning($"Log file write failed: {e.Message}");
            }
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        Flush();
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }

        _loggers.Clear();
    }
}
