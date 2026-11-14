using System.Collections.Concurrent;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Components.Reloading;

internal sealed class ComponentFileWatcher : IDisposable
{
    private readonly Timer _adapterTimer;
    private readonly FileSystemWatcher[] _adapterWatchers;
    private readonly ConcurrentDictionary<string, byte> _changedAdapterPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComponentReloadCoordinator _coordinator;
    private int _disposed;

    public ComponentFileWatcher(
        IEnumerable<string> adapterPaths,
        ComponentReloadCoordinator coordinator)
    {
        _coordinator = coordinator;
        var resolvedAdapterPaths = adapterPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _adapterTimer = new Timer(state => _ = ReloadAdapterAsync(), null, Timeout.Infinite, Timeout.Infinite);

        _adapterWatchers =
        [
            .. resolvedAdapterPaths
                .Select(Path.GetDirectoryName)
                .Where(directory => directory is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(directory => CreateWatcher(directory!, false, path =>
                {
                    var fullPath = Path.GetFullPath(path);
                    if (!resolvedAdapterPaths.Contains(fullPath)) return;
                    _changedAdapterPaths[fullPath] = 0;
                    _adapterTimer.Change(750, Timeout.Infinite);
                }))
        ];
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var watcher in _adapterWatchers) watcher.Dispose();
        _adapterTimer.Dispose();
    }

    private async Task ReloadAdapterAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        // Only the adapter whose DLL changed is reloaded; the others keep their connections.
        foreach (var path in _changedAdapterPaths.Keys)
        {
            if (!_changedAdapterPaths.TryRemove(path, out _)) continue;
            try
            {
                if (await _coordinator.ReloadChangedAdapterAsync(path).ConfigureAwait(false))
                    BotLog.Info($"检测到 Adapter 文件变化，热重载完成: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                BotLog.Error($"Adapter 自动热重载失败: {Path.GetFileName(path)} - {ex.Message}");
            }
        }
    }

    private static FileSystemWatcher CreateWatcher(
        string path,
        bool includeSubdirectories,
        Action<string> schedule)
    {
        var watcher = new FileSystemWatcher(path, "*.dll")
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += (_, e) => schedule(e.FullPath);
        watcher.Created += (_, e) => schedule(e.FullPath);
        watcher.Deleted += (_, e) => schedule(e.FullPath);
        watcher.Renamed += (_, e) => schedule(e.FullPath);
        return watcher;
    }
}