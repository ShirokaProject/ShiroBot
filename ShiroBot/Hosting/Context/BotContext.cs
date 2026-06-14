using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using ShiroBot.SDK.Adapter;

namespace ShiroBot.Hosting.Context;

internal sealed class BotContext
{
    private IReadOnlyList<long> _ownerList;
    private IReadOnlyList<long> _adminList;
    private IRenderContext? _renderer;
    private readonly IMessageService _messageService;

    public BotContext(IBotAdapter adapter, IReadOnlyList<long> ownerList, IReadOnlyList<long> adminList, IWebHostContext webHost)
    {
        File = new FileContext(adapter.File);
        Friend = new FriendContext(adapter.Friend);
        Group = new GroupContext(adapter.Group);
        _messageService = adapter.Message;
        ReplySubscriptions = new ReplySubscriptionManager();
        Message = new MessageContext(_messageService, ReplySubscriptions, "__host");
        System = new SystemContext(adapter.System);
        Updater = new UpdaterContext();
        WebHost = webHost;
        _ownerList = ownerList;
        _adminList = adminList;
    }

    public IFileContext File { get; }
    public IFriendContext Friend { get; }
    public IGroupContext Group { get; }
    public IMessageContext Message { get; }
    public ISystemContext System { get; }
    public IUpdater Updater { get; }
    public IWebHostContext WebHost { get; }

    public IReadOnlyList<long> OwnerList => Volatile.Read(ref _ownerList);
    public IReadOnlyList<long> AdminList => Volatile.Read(ref _adminList);

    /// <summary>
    /// 由 library plugin 注册的渲染服务。没有 library plugin 提供时为 null。
    /// </summary>
    public IRenderContext? Renderer => Volatile.Read(ref _renderer);

    internal ReplySubscriptionManager ReplySubscriptions { get; }

    internal IMessageContext CreatePluginMessageContext(string pluginName) =>
        new MessageContext(_messageService, ReplySubscriptions, pluginName);

    public void UpdateOwnerList(IReadOnlyList<long> ownerList)
    {
        Volatile.Write(ref _ownerList, ownerList);
    }

    public void UpdateAdminList(IReadOnlyList<long> adminList)
    {
        Volatile.Write(ref _adminList, adminList);
    }

    public void AttachRenderer(IRenderContext renderer)
    {
        Volatile.Write(ref _renderer, renderer);
    }
}
