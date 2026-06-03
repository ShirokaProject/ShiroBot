using ShiroBot.SDK.Config;

namespace ShiroBot.SDK.Plugin;

public interface IBotContext
{

    public IFileContext File { get; }
    public IFriendContext Friend { get; }
    public IGroupContext Group { get; }
    public IMessageContext Message { get; }  
    public ISystemContext System { get; }
    public IUpdater Updater { get; }
    public IConfigContext Config { get; }
    public IReadOnlyList<long> OwnerList { get; }
    public IReadOnlyList<long> AdminList { get; }

    /// <summary>
    /// 由 library plugin 提供的渲染服务。没有任何 library plugin 注册过时为 null。
    /// </summary>
    public IRenderContext? Render { get; }

    public bool IsOwner(long userId) => OwnerList.Contains(userId);

    public bool IsAdmin(long userId) => IsOwner(userId) || AdminList.Contains(userId);
}
