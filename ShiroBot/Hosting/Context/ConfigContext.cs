using ShiroBot.Core;
using ShiroBot.SDK.Config;
namespace ShiroBot.Hosting.Context;

internal sealed class ConfigContext : IConfigContext
{
    private readonly ConfigManager _configManager = new();
    private readonly string _displayName;

    public string ConfigPath { get; }

    private ConfigContext(string configPath, string displayName)
    {
        ConfigPath = Path.GetFullPath(configPath);
        _displayName = displayName;
    }

    private sealed class NullConfigContext : IConfigContext
    {
        public string ConfigPath => string.Empty;
        public T Load<T>() where T : class, new() => new T();
        public void Save<T>(T config) where T : class { }

        public IDisposable Watch<T>(Action<T> onChanged, int debounceMs = 500) where T : class, new()
        {
            // 没有真实配置文件，监听变成空操作。
            return new EmptySubscription();
        }

        private sealed class EmptySubscription : IDisposable
        {
            public void Dispose() { }
        }
    }

    public static IConfigContext NullConfig()
    {
        return new NullConfigContext();
    }

    public static IConfigContext ForCore(string coreConfigPath)
    {
        return new ConfigContext(coreConfigPath, "核心");
    }

    public static IConfigContext ForAdapter(string adapterConfigPath)
    {
        return new ConfigContext(adapterConfigPath, "适配器");
    }

    public static IConfigContext ForPlugin(string pluginConfigPath)
    {
        return new ConfigContext(pluginConfigPath, "插件");
    }

    public T Load<T>() where T : class, new()
    {
        return _configManager.LoadConfig<T>(ConfigPath, $"{_displayName}") ?? new T();
    }

    public void Save<T>(T config) where T : class
    {
        _configManager.SaveConfig(ConfigPath, config);
    }

    public IDisposable Watch<T>(Action<T> onChanged, int debounceMs = 500) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        var directory = Path.GetDirectoryName(ConfigPath)
                        ?? throw new InvalidOperationException($"无法解析配置文件目录: {ConfigPath}");
        Directory.CreateDirectory(directory);

        var effectiveDebounce = Math.Max(50, debounceMs);

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(ConfigPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // 防抖 timer + 重入互斥，避免编辑器原子写盘触发的多次 Changed 重叠 reload。
        var reloadGate = new SemaphoreSlim(1, 1);
        Timer? timer = null;
        // ReSharper disable once AccessToModifiedClosure
        timer = new Timer(_ => _ = ReloadAsync(), null, Timeout.Infinite, Timeout.Infinite);

        FileSystemEventHandler scheduleReload = (_, _) => timer!.Change(effectiveDebounce, Timeout.Infinite);
        RenamedEventHandler renamedHandler = (_, _) => timer!.Change(effectiveDebounce, Timeout.Infinite);
        ErrorEventHandler errorHandler = (_, args) =>
            ConsoleHelper.Warning($"{_displayName}配置热重载监听异常: {ConfigPath} - {args.GetException().Message}");

        watcher.Changed += scheduleReload;
        watcher.Created += scheduleReload;
        watcher.Renamed += renamedHandler;
        watcher.Error += errorHandler;

        return new ConfigWatchSubscription(
            watcher,
            timer!,
            () =>
            {
                watcher.Changed -= scheduleReload;
                watcher.Created -= scheduleReload;
                watcher.Renamed -= renamedHandler;
                watcher.Error -= errorHandler;
            });

        async Task ReloadAsync()
        {
            if (!await reloadGate.WaitAsync(0).ConfigureAwait(false))
            {
                // 已经有一次 reload 在跑，让它处理新的版本即可。
                timer!.Change(effectiveDebounce, Timeout.Infinite);
                return;
            }

            try
            {
                // 编辑器写盘瞬间文件可能为 0 字节或被独占，最多重试 5 次共 ~250ms。
                T? loaded = null;
                Exception? lastError = null;
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        loaded = Load<T>();
                        lastError = null;
                        break;
                    }
                    catch (IOException ex)
                    {
                        lastError = ex;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        break;
                    }

                    await Task.Delay(50).ConfigureAwait(false);
                }

                if (lastError is not null)
                {
                    ConsoleHelper.Error($"{_displayName}配置热重载失败: {ConfigPath} - {lastError.Message}");
                    return;
                }

                if (loaded is null)
                {
                    return;
                }

                try
                {
                    onChanged(loaded);
                }
                catch (Exception ex)
                {
                    ConsoleHelper.Error($"{_displayName}配置热重载回调失败: {ConfigPath} - {ex.Message}");
                }
            }
            finally
            {
                reloadGate.Release();
            }
        }
    }
}
