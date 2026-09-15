using ShiroBot.Configuration;
using ShiroBot.Console;
using ShiroBot.SDK.Config;
namespace ShiroBot.Hosting.Context;

internal sealed class ConfigContext : IConfigContext
{
    private readonly ConfigManager _configManager = new();
    private readonly string _displayName;
    private readonly PluginContext? _pluginOwner;

    public string ConfigPath { get; }

    private ConfigContext(string configPath, string displayName, PluginContext? pluginOwner = null)
    {
        ConfigPath = Path.GetFullPath(configPath);
        _displayName = displayName;
        _pluginOwner = pluginOwner;
    }

    private sealed class NullConfigContext : IConfigContext
    {
        public string ConfigPath => string.Empty;
        public T Load<T>() where T : class, new() => new();
        public void Save<T>(T config) where T : class { }
        public void SetValue(string keyPath, object? value) { }

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

    public static IConfigContext ForPlugin(string pluginConfigPath, PluginContext pluginOwner)
    {
        return new ConfigContext(pluginConfigPath, "插件", pluginOwner);
    }

    public T Load<T>() where T : class, new()
    {
        return _configManager.LoadConfig<T>(ConfigPath, _displayName) ?? new();
    }

    public void Save<T>(T config) where T : class
    {
        _configManager.SaveConfig(ConfigPath, config);
    }

    public void SetValue(string keyPath, object? value)
    {
        _configManager.SetConfigValue(ConfigPath, keyPath, value);
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
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
        };

        // 防抖 timer + 重入互斥，避免编辑器原子写盘触发的多次 Changed 重叠 reload。
        var reloadGate = new SemaphoreSlim(1, 1);
        ConfigWatchSubscription? subscription = null;
        Timer? timer = null;
        // ReSharper disable once AccessToModifiedClosure
        timer = new Timer(state => _ = ReloadAsync(), null, Timeout.Infinite, Timeout.Infinite);

        FileSystemEventHandler scheduleReload = (_, _) => subscription!.Schedule(effectiveDebounce);
        RenamedEventHandler renamedHandler = (_, _) => subscription!.Schedule(effectiveDebounce);
        ErrorEventHandler errorHandler = (_, args) =>
            ConsoleOutput.Warning($"{_displayName}配置热重载监听异常: {ConfigPath} - {args.GetException().Message}");

        watcher.Changed += scheduleReload;
        watcher.Created += scheduleReload;
        watcher.Renamed += renamedHandler;
        watcher.Error += errorHandler;

        subscription = new ConfigWatchSubscription(
            watcher,
            timer,
            reloadGate,
            () =>
            {
                watcher.Changed -= scheduleReload;
                watcher.Created -= scheduleReload;
                watcher.Renamed -= renamedHandler;
                watcher.Error -= errorHandler;
            },
            _pluginOwner is null ? null : _pluginOwner.UnregisterConfigWatch);

        if (_pluginOwner is not null)
        {
            _pluginOwner.RegisterConfigWatch(subscription);
        }
        else
        {
            subscription.Start();
        }

        return subscription;

        async Task ReloadAsync()
        {
            if (subscription?.IsDisposed != false)
            {
                return;
            }

            if (!await reloadGate.WaitAsync(0).ConfigureAwait(false))
            {
                // 已经有一次 reload 在跑，让它处理新的版本即可。
                subscription.Schedule(effectiveDebounce);
                return;
            }

            try
            {
                if (subscription.IsDisposed)
                {
                    return;
                }

                // 编辑器写盘瞬间文件可能为 0 字节或被独占，最多重试 5 次共 ~250ms。
                T? loaded = null;
                Exception? lastError = null;
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    if (subscription.IsDisposed)
                    {
                        return;
                    }

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

                if (subscription.IsDisposed)
                {
                    return;
                }

                if (lastError is not null)
                {
                    ConsoleOutput.Error($"{_displayName}配置热重载失败: {ConfigPath} - {lastError.Message}");
                    return;
                }

                if (loaded is null)
                {
                    return;
                }

                try
                {
                    if (subscription.IsDisposed)
                    {
                        return;
                    }

                    subscription.Invoke(onChanged, loaded);
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Error($"{_displayName}配置热重载回调失败: {ConfigPath} - {ex.Message}");
                }
            }
            finally
            {
                reloadGate.Release();
            }
        }
    }
}
