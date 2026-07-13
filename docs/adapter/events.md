# 上报事件

适配器通过 `IEventService` 将协议事件交给宿主。宿主会自动桥接接口公开的全部 `Func<TEvent, Task>` 事件，再按插件订阅与消息路由进行分发。

## 实现事件服务

```csharp
using ShiroBot.Model.Common;
using ShiroBot.SDK.Adapter;

public sealed class ExampleEventService : IEventService
{
    public event Func<GroupIncomingMessage, Task>? GroupMessageReceived;
    public event Func<FriendIncomingMessage, Task>? FriendMessageReceived;
    public event Func<MessageRecallEvent, Task>? MessageRecall;
    public event Func<GroupMemberIncreaseEvent, Task>? GroupMemberIncrease;
    public event Func<BotOfflineEvent, Task>? BotOffline;

    public Task PublishAsync(Event evt) => evt switch
    {
        GroupIncomingMessage message =>
            InvokeAsync(GroupMessageReceived, message),
        FriendIncomingMessage message =>
            InvokeAsync(FriendMessageReceived, message),
        MessageRecallEvent recalled =>
            InvokeAsync(MessageRecall, recalled),
        GroupMemberIncreaseEvent increased =>
            InvokeAsync(GroupMemberIncrease, increased),
        BotOfflineEvent offline =>
            InvokeAsync(BotOffline, offline),
        _ => Task.CompletedTask
    };

    private static Task InvokeAsync<TEvent>(
        Func<TEvent, Task>? handlers,
        TEvent evt)
    {
        if (handlers is null)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(
            handlers.GetInvocationList()
                .Cast<Func<TEvent, Task>>()
                .Select(handler => handler(evt)));
    }
}
```

只需要显式实现适配器实际能够产生的事件；其他事件可以使用接口默认的空访问器。

## 支持的事件

`IEventService` 当前定义：

- `GroupMessageReceived`
- `FriendMessageReceived`
- `MessageRecall`
- `FriendRequest`
- `GroupJoinRequest`
- `GroupInvitedJoinRequest`
- `GroupInvitation`
- `FriendNudge`
- `FriendFileUpload`
- `GroupAdminChange`
- `GroupEssenceMessageChange`
- `GroupMemberIncrease`
- `GroupMemberDecrease`
- `GroupNameChange`
- `GroupMessageReaction`
- `GroupMute`
- `GroupWholeMute`
- `GroupNudge`
- `GroupFileUpload`
- `PeerPinChange`
- `GroupDisband`
- `BotOffline`

## 从协议 DTO 转换

推荐把转换逻辑和传输层分开：

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

映射器应负责：

- 将协议事件名称转换为具体 ShiroBot 模型类型。
- 将消息内容转换为 `IncomingSegment[]`。
- 填写群、好友、发送者和消息序号。
- 统一时间单位与枚举值。
- 对协议新增字段保持向后兼容。

## 分发与背压

宿主事件桥接会把适配器事件投入后台分发，然后立即完成适配器事件回调。这避免慢插件直接阻塞协议接收循环，但适配器仍需要控制自己的读取速度和队列容量。

高流量适配器建议：

- 使用有界 `Channel<T>` 缓冲协议事件。
- 队列满时记录告警并定义明确策略，而不是无限占用内存。
- 保证同一消息不会因重连被重复上报，或为事件提供可去重标识。
- 断线重连使用指数退避并支持取消。

## 启动时序

宿主会在 `StartAsync()` 成功后才订阅 `IEventService`。如果协议客户端可能在 `StartAsync()` 内立刻产生事件，应先缓存在适配器内部，等启动返回并完成桥接后再排出，避免丢失最早的一批事件。
