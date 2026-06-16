using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal sealed class PluginContext : IBotContext, IDisposable
{
    private readonly string _pluginName;

    public IFileContext File => BotContext.File;
    public IFriendContext Friend => BotContext.Friend;
    public IGroupContext Group => BotContext.Group;
    public IMessageContext Message { get; }
    public ISystemContext System => BotContext.System;
    public IUpdater Updater => BotContext.Updater;
    public IWebHostContext WebHost => BotContext.WebHost;
    public string PluginDirectory { get; }
    public IConfigContext Config { get; private set; }
    public IReadOnlyList<long> OwnerList => BotContext.OwnerList;
    public IReadOnlyList<long> AdminList => BotContext.AdminList;
    public IRenderContext? Render => BotContext.Renderer;
    public IConsoleLogger Logger { get; }

    private BotContext BotContext { get; }

    public PluginContext(
        BotContext botContext,
        string pluginName,
        string pluginDirectory,
        Func<long, bool> groupRouteFilter,
        HostLogHub logHub)
    {
        BotContext = botContext;
        _pluginName = pluginName;
        Message = botContext.CreatePluginMessageContext(pluginName);
        Logger = new ConsoleLogger($"[Plugin:{pluginName}]", logHub);
        PluginDirectory = Path.GetFullPath(pluginDirectory);
        Directory.CreateDirectory(PluginDirectory);
        Config = ConfigContext.ForPlugin(Path.Combine(PluginDirectory, "config.toml"));
    }

    public void Dispose()
    {
        BotContext.ReplySubscriptions.UnregisterOwner(_pluginName);
        Config = null!;
    }
}
