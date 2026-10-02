using ShiroBot.Configuration;
using ShiroBot.Adapters;
using ShiroBot.Console;
using ShiroBot.SDK.Config;
using System.Reflection;
namespace ShiroBot.Hosting.Context;

internal sealed class ConfigContext : IConfigContext
{
    private static readonly MethodInfo LoadUntypedMethod = typeof(ConfigContext)
        .GetMethod(nameof(LoadUntypedGeneric), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo WatchUntypedMethod = typeof(ConfigContext)
        .GetMethod(nameof(WatchUntypedGeneric), BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly ConfigManager _configManager = new();
    private readonly string _displayName;
    private readonly string? _instanceId;
    private readonly PluginContext? _pluginOwner;

    public string ConfigPath { get; }

    private ConfigContext(string configPath, string displayName, PluginContext? pluginOwner = null, string? instanceId = null)
    {
        ConfigPath = Path.GetFullPath(configPath);
        _displayName = displayName;
        _pluginOwner = pluginOwner;
        _instanceId = instanceId;
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

    public static IConfigContext ForAdapter(string adapterConfigPath, string? instanceId = null)
    {
        return new ConfigContext(adapterConfigPath, "适配器", instanceId: AdapterInstanceStore.IsDeclared(adapterConfigPath) ? instanceId : null);
    }

    public static IConfigContext ForPlugin(string pluginConfigPath, PluginContext pluginOwner)
    {
        return new ConfigContext(pluginConfigPath, "插件", pluginOwner);
    }

    internal static object LoadUntyped(IConfigContext context, Type configType) =>
        LoadUntypedMethod.MakeGenericMethod(configType).Invoke(null, [context])
        ?? throw new InvalidOperationException($"无法加载配置类型: {configType.FullName}");

    internal static IDisposable WatchUntyped(IConfigContext context, Type configType, Action<object> onChanged) =>
        (IDisposable)(WatchUntypedMethod.MakeGenericMethod(configType).Invoke(null, [context, onChanged])
        ?? throw new InvalidOperationException($"无法监听配置类型: {configType.FullName}"));

    private static object LoadUntypedGeneric<T>(IConfigContext context) where T : class, new() => context.Load<T>();

    private static IDisposable WatchUntypedGeneric<T>(IConfigContext context, Action<object> onChanged)
        where T : class, new() => context.Watch<T>(value => onChanged(value));

    public T Load<T>() where T : class, new()
    {
        if (_instanceId is not null) return AdapterInstanceStore.Load<T>(ConfigPath, _instanceId);
        return _configManager.LoadConfig<T>(ConfigPath, _displayName) ?? new();
    }

    public void Save<T>(T config) where T : class
    {
        if (_instanceId is not null) AdapterInstanceStore.Save(ConfigPath, _instanceId, config);
        else _configManager.SaveConfig(ConfigPath, config);
    }

    public void SetValue(string keyPath, object? value)
    {
        if (_instanceId is not null) AdapterInstanceStore.Patch(ConfigPath, _instanceId, path => _configManager.SetConfigValue(path, keyPath, value));
        else _configManager.SetConfigValue(ConfigPath, keyPath, value);
    }

    public IDisposable Watch<T>(Action<T> onChanged, int debounceMs = 500) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        var directory = Path.GetDirectoryName(ConfigPath)
                        ?? throw new InvalidOperationException($"无法解析配置文件目录: {ConfigPath}");
        Directory.CreateDirectory(directory);

        var effectiveDebounce = Math.Max(50, debounceMs);
        var lastInstanceConfig = _instanceId is null ? null : System.Text.Json.JsonSerializer.Serialize(AdapterInstanceStore.GetConfig(ConfigPath, _instanceId));

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

                if (_instanceId is not null)
                {
                    var currentConfig = System.Text.Json.JsonSerializer.Serialize(AdapterInstanceStore.GetConfig(ConfigPath, _instanceId));
                    if (currentConfig == lastInstanceConfig) return;
                    lastInstanceConfig = currentConfig;
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
