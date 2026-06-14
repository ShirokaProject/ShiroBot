using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal class PluginContext : IBotContext, IDisposable
{
    private readonly string _pluginName;

    public IFileContext File => BotContext.File;
    public IFriendContext Friend => BotContext.Friend;
    public IGroupContext Group => BotContext.Group;
    public IMessageContext Message { get; }
    public ISystemContext System => BotContext.System;
    public IUpdater Updater => BotContext.Updater;
    public IWebHostContext WebHost => BotContext.WebHost;
    public IConfigContext Config { get; private set; }
    public IReadOnlyList<long> OwnerList => BotContext.OwnerList;
    public IReadOnlyList<long> AdminList => BotContext.AdminList;
    public IRenderContext? Render => BotContext.Renderer;
    public IConsoleLogger Logger { get; }

    protected BotContext BotContext { get; }

    public PluginContext(
        BotContext botContext,
        string pluginName,
        string? pluginDirectory,
        Func<long, bool> groupRouteFilter)
    {
        BotContext = botContext;
        _pluginName = pluginName;
        Message = botContext.CreatePluginMessageContext(pluginName);
        Logger = new ConsoleLogger($"[Plugin:{pluginName}]");
        Config = string.IsNullOrEmpty(pluginDirectory)
            ? ConfigContext.NullConfig()
            : ConfigContext.ForPlugin(Path.Combine(pluginDirectory, "config.toml"));
    }

    public virtual void Dispose()
    {
        BotContext.ReplySubscriptions.UnregisterOwner(_pluginName);
        Config = null!;
    }
}
