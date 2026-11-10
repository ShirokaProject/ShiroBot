using System.Text.Json.Nodes;

namespace ShiroBot.SDK.Plugin;

/// <summary>
///     插件之间的事件总线：按主题发布和订阅 JSON 消息，双方不需要引用任何共享程序集。
///     订阅随插件卸载自动取消；每个订阅者收到的是独立的副本。
/// </summary>
public interface IPluginEventBus
{
    /// <summary>订阅主题；返回的对象 Dispose 后取消订阅。处理器异常记录到订阅者的日志，不影响发布者。</summary>
    IDisposable On(string topic, Func<JsonNode?, CancellationToken, Task> handler);

    /// <summary>发布消息并等待所有订阅者处理完成，返回实际收到消息的订阅者数量。</summary>
    Task<int> EmitAsync(string topic, JsonNode? payload = null, CancellationToken cancellationToken = default);

    /// <summary>以 UTF-8 JSON 字节订阅，供强类型封装使用。</summary>
    IDisposable OnUtf8(string topic, PluginUtf8EventHandler handler)
    {
        throw new NotSupportedException("This host does not support the plugin event bus.");
    }

    /// <summary>以 UTF-8 JSON 字节发布，供强类型封装使用。</summary>
    Task<int> EmitUtf8Async(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("This host does not support the plugin event bus.");
    }
}