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
    private readonly StreamWriter? _writer;
    private readonly Timer? _flushTimer;
    private readonly object _lock = new();

    public string? FilePath { get; }

    public bool IsOpen => _writer != null;

    public FileLoggerProvider(string directory, string fileName, string header, LogLevelGate gate)
    {
        _gate = gate;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
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
            _flushTimer = new Timer(_ => Flush(), null, 1000, 1000);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Log file could not be opened: {e}");
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
            catch (Exception)
            {
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
            catch (Exception)
            {
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
            catch (Exception)
            {
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
        }

        _loggers.Clear();
    }
}
