using System.Security.Cryptography;
using System.Text;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Files;

/// <summary>Owns only cache/plugin-temp, never other cached metadata or plugin persistent state.</summary>
internal sealed class TemporaryFileManager : IDisposable
{
    private readonly string _root;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _directories = new(StringComparer.Ordinal);
    private readonly ITimer _timer;
    private bool _disposed;

    public TemporaryFileManager(string cacheDirectory, TimeProvider? timeProvider = null)
    {
        _root = Path.Combine(Path.GetFullPath(cacheDirectory), "plugin-temp");
        _time = timeProvider ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        EnsureRootIsNotLink();
        // Start before loading plugins: even unexpired files from a crashed/terminated process are stale.
        if (Directory.Exists(_root))
        {
            foreach (var directory in Directory.EnumerateDirectories(_root))
                if (!TryDeleteDirectory(directory)) _directories[directory] = DateTimeOffset.MinValue;
            foreach (var file in Directory.EnumerateFiles(_root))
            {
                try { File.Delete(file); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { BotLog.Warning("临时文件启动清理失败，将在下次启动重试: " + error.GetType().Name); }
            }
        }
        Directory.CreateDirectory(_root);
        _timer = _time.CreateTimer(_ => CleanupExpired(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public TemporaryDirectory CreateDirectory(string owner, TimeSpan retention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        // IDs cannot introduce separators or collide through filename normalization.
        var ownerHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))).ToLowerInvariant();
        if (retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention), "Retention must be positive.");
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var expiresAt = _time.GetUtcNow().Add(retention);
            EnsureRootIsNotLink();
            // Use a flat unique directory so one plugin cannot name another plugin's directory.
            var path = Path.Combine(_root, ownerHash + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            _directories.Add(path, expiresAt);
            return new TemporaryDirectory(path, expiresAt);
        }
    }

    internal void CleanupExpired()
    {
        lock (_lock)
        {
            if (_disposed) return;
            try { EnsureRootIsNotLink(); }
            catch (IOException error)
            {
                BotLog.Warning("临时文件清理拒绝访问替换后的缓存目录: " + error.GetType().Name);
                return;
            }
            var now = _time.GetUtcNow();
            foreach (var (path, expiresAt) in _directories.ToArray())
                if (expiresAt <= now && TryDeleteDirectory(path)) _directories.Remove(path);
        }
    }

    private void EnsureRootIsNotLink()
    {
        // Never follow a replaced root/ancestor into persistent or external directories.
        for (var directory = new DirectoryInfo(_root); directory is not null; directory = directory.Parent)
        {
            // macOS /var and /tmp are system symlinks; reject only our owned cache paths.
            if (directory.FullName != _root && directory.FullName != Path.GetDirectoryName(_root)) break;
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Temporary cache directory cannot be a symbolic link.");
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            BotLog.Warning("临时目录清理失败，将自动重试: " + error.GetType().Name);
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
        // Ctrl+C cleanup is best effort; the next process retries everything left here.
        lock (_lock)
        {
            try { EnsureRootIsNotLink(); }
            catch (IOException) { return; }
            foreach (var path in _directories.Keys) TryDeleteDirectory(path);
            _directories.Clear();
        }
    }
}
