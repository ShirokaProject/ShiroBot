# 上报事件

适配器通过 `IEventService.EventReceived` 向宿主上报 `ShiroBot.SDK.Models.BotEvent`。宿主在调用 `StartAsync()` 前订阅此事件，因此适配器启动连接后即可发布最早收到的事件。

## 实现事件服务

```csharp
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;

public sealed class MyEventService : IEventService
{
    public event Func<BotEvent, Task>? EventReceived;

    public async Task PublishAsync(BotEvent botEvent)
    {
        var handlers = EventReceived;
        if (handlers is null) return;

        foreach (Func<BotEvent, Task> handler in handlers.GetInvocationList())
            await handler(botEvent);
    }
}
```

适配器将协议事件映射为通用事件，再调用 `PublishAsync`。每个事件的 `Platform` 必须与 `IBotAdapter.Platform` 相同；`SelfId` 是当前机器人账号 ID。宿主转发时以实际适配器 Platform 覆盖该字段，并填入当前实例的 `AdapterId`，适配器不用自行填写实例 ID。同 DLL 多个对象上报的事件，即使 Platform 和 SelfId 相同，也按实例区分。

```csharp
await _events.PublishAsync(new MessageEvent
{
    Platform = "qq",
    SelfId = selfId,
    MessageId = messageSeq.ToString(),
    Channel = Channel.Group(groupId.ToString()),
    Sender = new User(senderId.ToString()) { Name = senderName },
    Segments = [new TextSegment(text)],
    Timestamp = receivedAt
});
```

示例中的 `selfId`、`messageSeq`、`groupId`、`senderId`、`senderName`、`text` 和 `receivedAt` 来自协议客户端。实际映射需处理完整的消息段、群成员和平台原始负载，见[适配不同 Model](/adapter/models)。

## 选择事件类型

| 协议事件 | 通用类型 |
| --- | --- |
| 收到消息 | `MessageEvent` |
| 消息撤回或删除 | `MessageDeletedEvent` |
| 成员加入或离开 | `MemberJoinedEvent` / `MemberLeftEvent` |
| 好友请求 | `FriendRequestEvent` |
| 机器人收到群或服务器邀请 | `GuildInviteEvent` |
| 机器人离线 | `BotOfflineEvent` |
| 仅该平台具有的事件 | `PlatformEvent`，用 `Kind` 区分 |

通用事件有平台特有字段时，可把平台 Model 对象放进 `BotEvent.Raw`。QQ 戳一戳等没有通用事件类型的情况，用 `PlatformEvent { Kind = QEventKinds.GroupNudge, Raw = qGroupNudge }`。不要把同一事件同时作为通用事件和 `PlatformEvent` 重复上报，除非插件确实需要两次独立通知。

## 接收循环

宿主接收适配器事件后使用有界队列分发，适配器仍需处理协议客户端自己的流控、取消、断线重连和去重。`PublishAsync` 应等待事件订阅者完成入队；不要在协议回调中悄悄丢弃异常。`StopAsync()` 应停止接收循环并释放连接。
