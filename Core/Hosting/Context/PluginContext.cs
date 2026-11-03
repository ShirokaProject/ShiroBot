using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;
using ShiroBot.Hosting.Logging;
using ShiroBot.Plugins.Dashboard;
using ShiroBot.Plugins.Services;

namespace ShiroBot.Hosting.Context;

internal sealed class PluginContext : IBotContext, IDisposable
{
    private readonly string _pluginName;
    private readonly object _configWatchLock = new();
    private readonly HashSet<ConfigWatchSubscription> _configWatches = [];
    private readonly PluginServiceRegistry _serviceRegistry;
    private readonly PluginInteropOwner _interopOwner;
    private readonly PluginLifetimeScope _lifetime = new();
    private Func<Func<Task>, Task<bool>>? _dispatchGuard;
    private int _externalCallbacksDetached;
    private int _disposed;
    private bool _configWatchesDisposed;

    public string Platform => BotContext.Platform;
    public string? InstanceId => BotContext.InstanceId;
    public AdapterInstanceInfo? AdapterInstance => BotContext.AdapterInstance;
    public IReadOnlyList<AdapterInstanceInfo> GetAdapterInstances() => BotContext.GetAdapterInstances();
    public IMessageContext Message { get; }
    public IChannelService Channel => BotContext.Channel;
    public IUserService User => BotContext.User;
    public IUpdater Updater => BotContext.Updater;
    public IWebHostContext WebHost => BotContext.WebHost;
    public IPluginServices Services { get; }
    public IPluginLifetime Lifetime => _lifetime;
    public IPluginEventBus Bus { get; }
    public IPluginDashboard Dashboard => DashboardScope;
    internal PluginDashboardScope DashboardScope { get; }
    public TemporaryDirectory CreateTempDirectory(TimeSpan retention)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return (BotContext.TemporaryFiles ?? throw new InvalidOperationException("Host temporary file service was not initialized."))
            .CreateDirectory(_pluginName, retention);
    }
    public string PluginDirectory { get; }
    public IConfigContext Config { get; private set; }
    public IReadOnlyList<UserReference> OwnerList => BotContext.OwnerList;
    public IReadOnlyList<UserReference> AdminList => BotContext.AdminList;
    public bool IsOwner(UserReference user) => BotContext.IsOwner(user);
    public bool IsAdmin(UserReference user) => BotContext.IsAdmin(user);
    public bool IsOwner(string userId) => BotContext.IsOwner(userId);
    public bool IsAdmin(string userId) => BotContext.IsAdmin(userId);
    public IRenderContext? Render => BotContext.Renderer;
    public HostStatus? GetHostStatus() => BotContext.GetHostStatus();
    public IConsoleLogger Logger { get; }

    public TService? GetAdapterExtension<TService>() where TService : class =>
        BotContext.GetAdapterExtension<TService>();

    public IDisposable UseInstance(string instanceId) => BotContext.UseInstance(instanceId);

    private BotContext BotContext { get; }

    public PluginContext(
        BotContext botContext,
        string pluginName,
        string pluginDirectory,
        HostLogHub logHub,
        PluginServiceRegistry serviceRegistry)
    {
        BotContext = botContext;
        _pluginName = pluginName;
        Message = botContext.CreatePluginMessageContext(pluginName);
        Logger = new ConsoleLogger($"[Plugin:{pluginName}]", logHub);
        _serviceRegistry = serviceRegistry;
        _interopOwner = new PluginInteropOwner(pluginName);
        Services = new PluginServiceScope(serviceRegistry, _interopOwner, InvokeAsync, _lifetime);
        Bus = new PluginEventBusScope(serviceRegistry, _interopOwner, InvokeAsync, _lifetime);
        DashboardScope = new PluginDashboardScope(InvokeAsync, _lifetime);
        PluginDirectory = Path.GetFullPath(pluginDirectory);
        Directory.CreateDirectory(PluginDirectory);
        Config = ConfigContext.ForPlugin(Path.Combine(PluginDirectory, "config.toml"), this);
    }

    /// <summary>
    /// 插件加载完成后由 LoadedPluginHandle 接入：其他插件的按名称调用和事件处理计入本插件正在处理的调用，
    /// 卸载会等待它们结束；插件开始卸载后不再执行。
    /// </summary>
    internal void AttachDispatchGuard(Func<Func<Task>, Task<bool>> guard) => _dispatchGuard = guard;

    private async Task<bool> InvokeAsync(Func<Task> work)
    {
        if (_dispatchGuard is { } guard) return await guard(work).ConfigureAwait(false);
        // 插件 OnLoad 期间还没有 handle：直接在插件日志作用域内执行。
        if (_externalCallbacksDetached != 0) return false;
        await BotLog.RunScoped(Logger, work).ConfigureAwait(false);
        return true;
    }

    /// <summary>取消并等待插件登记的后台任务和计时器；插件 OnUnload 之前调用，此时上下文仍然可用。</summary>
    internal Task StopLifetimeTasksAsync() => _lifetime.StopTasksAsync();

    /// <summary>按登记的相反顺序释放插件登记的资源；插件 OnUnload 之后调用。</summary>
    internal Task DisposeLifetimeAsync() => _lifetime.DisposeResourcesAsync();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DetachExternalCallbacks();
        // 宿主的卸载流程已经等待过；直接 Dispose 时（回滚、测试）在后台收尾。
        var lifetime = _lifetime.DisposeResourcesAsync();
        if (!lifetime.IsCompleted)
            _ = lifetime.ContinueWith(task => Logger.Warning($"插件生命周期收尾失败: {task.Exception?.InnerException?.Message}"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        ((IDisposable)Services).Dispose();
        _dispatchGuard = null;
        Config = null!;
    }

    internal void DetachExternalCallbacks()
    {
        if (Interlocked.Exchange(ref _externalCallbacksDetached, 1) != 0)
        {
            return;
        }

        DisposeConfigWatches();
        // 先发出停止信号并停止接收其他插件的调用和事件；等待和释放在卸载流程后面进行。
        _lifetime.Cancel();
        _serviceRegistry.DetachInterop(_interopOwner);
        BotContext.ReplySubscriptions.UnregisterOwner(_pluginName);
        BotContext.WebHost.UnregisterOwner(_pluginName);
    }

    internal void RegisterConfigWatch(ConfigWatchSubscription subscription)
    {
        lock (_configWatchLock)
        {
            if (!_configWatchesDisposed)
            {
                _configWatches.Add(subscription);
                try
                {
                    subscription.Start();
                    return;
                }
                catch
                {
                    _configWatches.Remove(subscription);
                    subscription.Dispose();
                    throw;
                }
            }
        }

        subscription.Dispose();
    }

    internal void UnregisterConfigWatch(ConfigWatchSubscription subscription)
    {
        lock (_configWatchLock)
        {
            _configWatches.Remove(subscription);
        }
    }

    private void DisposeConfigWatches()
    {
        ConfigWatchSubscription[] subscriptions;
        lock (_configWatchLock)
        {
            _configWatchesDisposed = true;
            subscriptions = [.. _configWatches];
            _configWatches.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to dispose config watcher: {ex.Message}");
            }
        }
    }
}
