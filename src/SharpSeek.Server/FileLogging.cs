using Microsoft.Extensions.Logging;

namespace SharpSeek.Server;

/// <summary>
/// A minimal, dependency-free file logging provider. Appends log lines to a file and rotates to a
/// single <c>.old</c> backup when it exceeds a size cap, so the log stays bounded on a
/// long-running server. Writes are serialised and flushed per line for crash-safety.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private StreamWriter _writer;

    public FileLoggerProvider(string path, long maxBytes = 10 * 1024 * 1024)
    {
        _path = Path.GetFullPath(path);
        _maxBytes = maxBytes;

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = Open(_path);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Write(string line)
    {
        lock (_gate)
        {
            _writer.WriteLine(line);
            if (_writer.BaseStream.Length >= _maxBytes)
            {
                Rotate();
            }
        }
    }

    private void Rotate()
    {
        _writer.Dispose();
        string backup = _path + ".old";
        File.Delete(backup);
        File.Move(_path, backup);
        _writer = Open(_path);
    }

    private static StreamWriter Open(string path) =>
        new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}

/// <summary>An <see cref="ILogger"/> that writes formatted lines through a <see cref="FileLoggerProvider"/>.</summary>
internal sealed class FileLogger : ILogger
{
    private readonly FileLoggerProvider _provider;
    private readonly string _category;

    public FileLogger(FileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

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

        string message = formatter(state, exception);
        string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{Abbreviate(logLevel)}] {_category}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        _provider.Write(line);
    }

    private static string Abbreviate(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };
}
