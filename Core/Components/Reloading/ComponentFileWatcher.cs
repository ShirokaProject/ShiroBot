using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Components.Reloading;

internal sealed class ComponentFileWatcher : IDisposable
{
    private readonly FileSystemWatcher[] _adapterWatchers;
    private readonly Timer _adapterTimer;
    private readonly ComponentReloadCoordinator _coordinator;
    private int _disposed;

    public ComponentFileWatcher(
        IEnumerable<string> adapterPaths,
        ComponentReloadCoordinator coordinator)
    {
        _coordinator = coordinator;
        var resolvedAdapterPaths = adapterPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _adapterTimer = new Timer(state => _ = ReloadAdapterAsync(), null, Timeout.Infinite, Timeout.Infinite);

        _adapterWatchers = resolvedAdapterPaths
            .Select(Path.GetDirectoryName)
            .Where(directory => directory is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(directory => CreateWatcher(directory!, includeSubdirectories: false, path =>
            {
                if (resolvedAdapterPaths.Contains(Path.GetFullPath(path)))
                {
                    _adapterTimer.Change(750, Timeout.Infinite);
                }
            })).ToArray();
    }

    private async Task ReloadAdapterAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            await _coordinator.ReloadAdapterAsync().ConfigureAwait(false);
            BotLog.Info("检测到 Adapter 文件变化，热重载完成。");
        }
        catch (Exception ex)
        {
            BotLog.Error("Adapter 自动热重载失败: " + ex.Message);
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var watcher in _adapterWatchers) watcher.Dispose();
        _adapterTimer.Dispose();
    }
}
