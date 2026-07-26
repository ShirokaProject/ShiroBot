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
    public IWebHostContext WebHost { get; }
    public IPluginServices Services { get; }
    public string PluginDirectory { get; }
    public IReadOnlyList<long> OwnerList { get; }
    public IReadOnlyList<long> AdminList { get; }

    /// <summary>
    /// 由宿主提供的渲染服务。渲染集成未启用时为 null。
    /// </summary>
    public IRenderContext? Render { get; }

    public bool IsOwner(long userId) => OwnerList.Contains(userId);

    public bool IsAdmin(long userId) => IsOwner(userId) || AdminList.Contains(userId);
}
