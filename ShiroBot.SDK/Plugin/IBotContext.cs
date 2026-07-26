using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;

namespace ShiroBot.SDK.Plugin;

public interface IBotContext
{
    /// <summary>当前适配器的平台 ID（如 "qq"、"discord"、"telegram"）。</summary>
    public string Platform { get; }

    public IMessageContext Message { get; }
    public IChannelService Channel { get; }
    public IUserService User { get; }
    public IUpdater Updater { get; }
    public IConfigContext Config { get; }
    public IWebHostContext WebHost { get; }
    public IPluginServices Services { get; }
    public string PluginDirectory { get; }
    public IReadOnlyList<string> OwnerList { get; }
    public IReadOnlyList<string> AdminList { get; }

    /// <summary>
    /// 获取适配器的平台特有扩展服务。适配器未实现时返回 null，插件应做能力探测。
    /// </summary>
    public TService? GetAdapterExtension<TService>() where TService : class;

    /// <summary>
    /// 由宿主提供的渲染服务。渲染集成未启用时为 null。
    /// </summary>
    public IRenderContext? Render { get; }

    public bool IsOwner(string userId) => OwnerList.Contains(userId);

    public bool IsAdmin(string userId) => IsOwner(userId) || AdminList.Contains(userId);
}
