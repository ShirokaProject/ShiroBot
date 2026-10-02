using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;

namespace ShiroBot.SDK.Plugin;

public interface IBotContext
{
    /// <summary>
    /// 当前适配器的平台 ID（如 "qq"、"discord"、"telegram"）。事件处理期间为事件来源；
    /// 后台调用未使用 <see cref="UseInstance"/> 时为宿主默认适配器。
    /// </summary>
    public string Platform { get; }

    /// <summary>当前适配器实例 ID，与平台账号 ID 不同；未加载适配器或旧宿主返回 null。</summary>
    public string? InstanceId => null;

    /// <summary>
    /// 当前作用域的适配器实例：事件处理期间为事件来源，<see cref="UseInstance"/> 内为所选实例，
    /// 否则为宿主默认适配器。未加载适配器或旧宿主返回 null。
    /// </summary>
    public AdapterInstanceInfo? AdapterInstance => null;

    /// <summary>当前已加载、可用 <see cref="UseInstance"/> 选择的全部适配器实例；每次调用返回新的快照。旧宿主返回空列表。</summary>
    public IReadOnlyList<AdapterInstanceInfo> GetAdapterInstances() => [];

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

    /// <summary>选择适配器实例，作用域跨 await 传递，Dispose 后恢复原选择。</summary>
    public IDisposable UseInstance(string instanceId) =>
        throw new NotSupportedException("This host does not support adapter instance selection.");

    /// <summary>
    /// 由宿主提供的渲染服务。渲染集成未启用时为 null。
    /// </summary>
    public IRenderContext? Render { get; }

    public bool IsOwner(string userId) => OwnerList.Contains(userId);

    public bool IsAdmin(string userId) => IsOwner(userId) || AdminList.Contains(userId);
}
