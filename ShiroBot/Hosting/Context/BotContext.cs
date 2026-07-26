using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal sealed class BotContext
{
    private IReadOnlyList<string> _ownerList;
    private IReadOnlyList<string> _adminList;
    private IRenderContext? _renderer;
    private readonly IBotAdapter _adapter;

    public BotContext(IBotAdapter adapter, IReadOnlyList<string> ownerList, IReadOnlyList<string> adminList, IWebHostContext webHost)
    {
        _adapter = adapter;
        Channel = adapter.Channel;
        User = adapter.User;
        ReplySubscriptions = new ReplySubscriptionManager();
        Message = new MessageContext(adapter.Message, ReplySubscriptions, "__host");
        Updater = new UpdaterContext();
        WebHost = webHost;
        _ownerList = ownerList;
        _adminList = adminList;
    }

    public string Platform => _adapter.Platform;
    public IMessageContext Message { get; }
    public IChannelService Channel { get; }
    public IUserService User { get; }
    public IUpdater Updater { get; }
    public IWebHostContext WebHost { get; }

    public IReadOnlyList<string> OwnerList => Volatile.Read(ref _ownerList);
    public IReadOnlyList<string> AdminList => Volatile.Read(ref _adminList);

    /// <summary>
    /// 由宿主渲染集成提供的服务。渲染集成未启用时为 null。
    /// </summary>
    public IRenderContext? Renderer => Volatile.Read(ref _renderer);

    internal ReplySubscriptionManager ReplySubscriptions { get; }

    internal IMessageContext CreatePluginMessageContext(string pluginName) =>
        new MessageContext(_adapter.Message, ReplySubscriptions, pluginName);

    internal TService? GetAdapterExtension<TService>() where TService : class =>
        _adapter.GetExtension<TService>();

    public void UpdateOwnerList(IReadOnlyList<string> ownerList)
    {
        Volatile.Write(ref _ownerList, ownerList);
    }

    public void UpdateAdminList(IReadOnlyList<string> adminList)
    {
        Volatile.Write(ref _adminList, adminList);
    }

    public void AttachRenderer(IRenderContext renderer)
    {
        Volatile.Write(ref _renderer, renderer);
    }
}
