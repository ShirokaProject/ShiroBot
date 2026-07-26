using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Core;

public interface IBotAdapter
{
    IConfigContext Config { get; set; }
    IConsoleLogger Logger { get; set; }

    /// <summary>平台 ID（如 "qq"、"discord"、"telegram"）。适配器上报的所有事件的 Platform 必须与此一致。</summary>
    string Platform { get; }

    public IMessageService Message { get; }
    public IChannelService Channel { get; }
    public IUserService User { get; }
    public IEventService Event { get; }

    /// <summary>
    /// 获取平台特有的扩展服务（如 QQ 的戳一戳、群文件；Discord 的 Reaction 等）。
    /// 适配器未实现该扩展时返回 null，插件应做能力探测。
    /// </summary>
    TService? GetExtension<TService>() where TService : class => this as TService;

    Task StartAsync() => Task.CompletedTask;
    Task StopAsync() => Task.CompletedTask;
}
