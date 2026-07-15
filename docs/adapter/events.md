# 上报事件

适配器通过 `IEventService.EventReceived` 将统一的 `Event` 模型交给宿主。新增模型事件不再要求 SDK、宿主和适配器分别增加 typed event。

## 实现事件服务

```csharp
using ShiroBot.Model.Common;
using ShiroBot.SDK.Adapter;

public sealed class ExampleEventService : IEventService
{
    public event Func<Event, Task>? EventReceived;

    public async Task PublishAsync(Event evt)
    {
        var handlers = EventReceived;
        if (handlers is null) return;

        foreach (Func<Event, Task> handler in handlers.GetInvocationList())
        {
            await handler(evt);
        }
    }
}
```

协议层收到事件后只需转换为对应模型并发布：

```csharp
private async Task OnProtocolEventAsync(ProtocolEvent raw)
{
    var evt = ProtocolEventMapper.Map(raw);
    if (evt is null)
    {
        _logger.Warning($"未支持的协议事件: {raw.Type}");
        return;
    }

    await _eventService.PublishAsync(evt);
}
```

Milky 模型生成器会根据 IR 生成 `EventMetadataRegistry`，其中包含 `event_type` 和 `message_scene` 到模型类型的映射。协议新增事件后重新生成 Model 即可，不需要维护手写事件字典。

## 分发与背压

宿主把适配器事件投入后台分发并立即完成回调，避免慢插件阻塞协议接收循环。适配器仍应：

- 使用有界 `Channel<Event>` 控制高流量事件。
- 明确队列满时的丢弃或等待策略。
- 对断线重连使用退避和取消。
- 保证同一消息不会因重连重复上报，或提供可去重标识。

`EventReceived` 的多个订阅者应按注册顺序等待，避免适配器内部制造无序并发。

## 启动时序

宿主会先订阅 `EventReceived`，再调用适配器 `StartAsync()`。适配器可以在启动期间建立事件连接，不会丢失最早一批事件。
