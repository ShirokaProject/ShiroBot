# 实现服务接口

适配器固定提供三组通用服务和一组事件服务。通用服务的方法有默认实现；只实现协议端真正支持的方法，其他方法调用时会抛 `NotSupportedException`。`IEventService.EventReceived` 是必须声明的事件。

| 接口 | 当前方法 | 映射目标 |
| --- | --- | --- |
| `IMessageService` | `SendMessageAsync`、`DeleteMessageAsync`、`GetMessageAsync`、`GetHistoryMessagesAsync`、`GetResourceUrlAsync` | 通用 `MessageSegment`、`MessageEvent` 和 `SentMessage` |
| `IChannelService` | `GetChannelsAsync`、`GetChannelAsync`、`GetMembersAsync`、`GetMemberAsync`、`SetChannelNameAsync`、`KickMemberAsync`、`MuteMemberAsync`、`LeaveChannelAsync` | 群、频道、话题及其成员 |
| `IUserService` | `GetSelfAsync`、`GetUserAsync`、`GetFriendsAsync`、`AcceptFriendRequestAsync`、`RejectFriendRequestAsync` | 机器人、用户、好友和好友请求 |
| `IEventService` | `EventReceived` | 向宿主发布 `BotEvent`；实现方式见[上报事件](/adapter/events) |

`IBotAdapter` 的 `Config`、`Logger`、`Platform`、四个服务属性与生命周期要求见[创建适配器](/adapter/)。平台特有能力不应塞入通用服务：QQ 的戳一戳、群公告和群文件等，按需实现 [QQ Model 扩展接口](/plugin/qq-model)并通过 `GetExtension<TService>()` 暴露。

## 消息服务的输入和输出

`SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments)` 接收平台无关的会话和消息段。适配器应按原顺序转换文本、@、引用、图片等段，再把协议返回的消息 ID 包装成 `SentMessage`：

```csharp
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;

public sealed class MyMessageService : IMessageService
{
    public async Task<SentMessage> SendMessageAsync(
        Channel channel, IReadOnlyList<MessageSegment> segments)
    {
        // 按 channel.Type 选择协议目标，按顺序转换 segments。
        var protocolMessageId = await SendToProtocolAsync(channel, segments);
        return new SentMessage(protocolMessageId.ToString());
    }
}
```

`SendToProtocolAsync` 代表适配器自己的协议调用，示例需替换为实际实现。`DeleteMessageAsync`、查询消息与历史消息使用同一套 ID 映射；收到平台事件时也要使用相同的 `MessageId` 和 `Channel.Id`。

## 映射时检查

- **ID**：通用模型用字符串保存用户、会话和消息 ID。转换 QQ 的 `long`、Discord 的 `ulong` 等数字时保持原值，不要经浮点数中转。
- **会话类型**：私聊用 `ChannelType.Direct`，群或频道用 `Group`，话题用 `Thread`；平台无法归类的会话用 `Other`。
- **消息段**：保留顺序和资源 ID。无法无损转换的平台段可用 `RawSegment(platform, kind, payload)` 保留。
- **时间**：把协议时间单位和时区转换为 `DateTimeOffset`。
- **能力边界**：协议不支持的方法保留默认 `NotSupportedException`，不要返回伪造的成功结果。

具体的 QQ、Discord、Telegram 映射与 Model 依赖见[适配不同 Model](/adapter/models)。
