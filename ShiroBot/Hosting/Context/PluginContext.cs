using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal sealed class PluginContext : IBotContext, IDisposable
{
    private readonly string _pluginName;
    private int _externalCallbacksDetached;

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

    private BotContext BotContext { get; }

    public PluginContext(
        BotContext botContext,
        string pluginName,
        string pluginDirectory,
        Func<string, bool> groupRouteFilter,
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
        Config = ConfigContext.ForPlugin(Path.Combine(PluginDirectory, "config.toml"));
    }

    public void Dispose()
    {
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

        BotContext.ReplySubscriptions.UnregisterOwner(_pluginName);
        BotContext.WebHost.UnregisterOwner(_pluginName);
    }
}
