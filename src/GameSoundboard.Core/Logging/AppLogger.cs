using System.Threading.Channels;

namespace GameSoundboard.Core.Logging;

public sealed class AppLogger : IDisposable
{
    private readonly Channel<string> _entries = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly Task _writer;
    private readonly string _path;
    private bool _disposed;

    public AppLogger(string? directory = null)
    {
        var logDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameSoundboard", "logs");
        _path = Path.Combine(logDirectory, $"GameSoundboard-{DateTime.Now:yyyy-MM-dd}.log");
        _writer = Task.Run(WriteLoopAsync);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        if (_disposed) return;
        _entries.Writer.TryWrite($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}");
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read,
                bufferSize: 4096, options: FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream) { AutoFlush = true };
            await foreach (var entry in _entries.Reader.ReadAllAsync()) await writer.WriteLineAsync(entry);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _entries.Writer.TryWrite($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [INFO] 程序退出");
        _disposed = true;
        _entries.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
    }
}
