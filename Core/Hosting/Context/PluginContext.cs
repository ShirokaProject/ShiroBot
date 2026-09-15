using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;
using ShiroBot.Hosting.Logging;
using ShiroBot.Plugins.Services;

namespace ShiroBot.Hosting.Context;

internal sealed class PluginContext : IBotContext, IDisposable
{
    private readonly string _pluginName;
    private readonly object _configWatchLock = new();
    private readonly HashSet<ConfigWatchSubscription> _configWatches = [];
    private int _externalCallbacksDetached;
    private int _disposed;
    private bool _configWatchesDisposed;

    public string Platform => BotContext.Platform;
    public IMessageContext Message { get; }
    public IChannelService Channel => BotContext.Channel;
    public IUserService User => BotContext.User;
    public IUpdater Updater => BotContext.Updater;
    public IWebHostContext WebHost => BotContext.WebHost;
    public IPluginServices Services { get; }
    public string PluginDirectory { get; }
    public IConfigContext Config { get; private set; }
    public IReadOnlyList<string> OwnerList => BotContext.OwnerList;
    public IReadOnlyList<string> AdminList => BotContext.AdminList;
    public IRenderContext? Render => BotContext.Renderer;
    public IConsoleLogger Logger { get; }

    public TService? GetAdapterExtension<TService>() where TService : class =>
        BotContext.GetAdapterExtension<TService>();

    public IDisposable UsePlatform(string platform) => BotContext.UsePlatform(platform);

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
        Services = new PluginServiceScope(serviceRegistry, pluginName);
        PluginDirectory = Path.GetFullPath(pluginDirectory);
        Directory.CreateDirectory(PluginDirectory);
        Config = ConfigContext.ForPlugin(Path.Combine(PluginDirectory, "config.toml"), this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DetachExternalCallbacks();
        ((IDisposable)Services).Dispose();
        Config = null!;
    }

    internal void DetachExternalCallbacks()
    {
        if (Interlocked.Exchange(ref _externalCallbacksDetached, 1) != 0)
        {
            return;
        }

        DisposeConfigWatches();
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
