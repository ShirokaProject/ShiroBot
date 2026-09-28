# 通用 Model

跨适配器的消息、事件、用户和会话模型位于 `ShiroBot.SDK.Models`。插件优先使用这些类型；只有需要协议特有字段或操作时，才进入 [QQ Model](/plugin/qq-model)、[Discord Model](/plugin/discord-model) 等平台模型。

## 消息与会话

| 类型 | 作用 |
| --- | --- |
| `Channel` / `ChannelType` | 会话 ID 与类型；`Channel.Direct(id)`、`Channel.Group(id)` 可快速构造 |
| `MessageEvent` | 入站消息，包含 `MessageId`、`Channel`、`Sender`、`Segments`、`Timestamp` |
| `SentMessage` | 出站结果，包含字符串 `MessageId` 与可选时间 |
| `User` / `Member` | 用户信息与频道内成员信息 |

```csharp
using ShiroBot.SDK.Models;

var text = message.GetPlainText();
var mentionsBot = message.HasMention(selfId);
var quote = message.GetQuote();
var images = message.Segments.OfType<ImageSegment>().ToArray();

await Context.Message.SendMessageAsync(
    Channel.Group(groupId),
    new TextSegment("图片："),
    new ImageSegment("https://example.com/a.png"));
```

`MessageEvent.IsDirect` 区分私聊；消息 ID 与通用用户、会话 ID 均以字符串保存，不要假定所有平台都是数字 ID。

## 消息段

| 类型 | 内容 |
| --- | --- |
| `TextSegment` | 文本 |
| `MentionSegment` / `MentionAllSegment` | 提及用户或全体成员 |
| `QuoteSegment` | 引用消息 ID |
| `EmojiSegment` | 平台表情 ID 与可选降级文本 |
| `ImageSegment` / `AudioSegment` / `VideoSegment` / `FileSegment` | 媒体 URI 与可选元数据 |
| `RawSegment` | 通用模型不能表示的平台原生段，含 `Platform`、`Kind`、`Payload` |

适配器决定各消息段能否发送。插件收到 `RawSegment` 时，应先检查平台与负载类型再转型。

## 事件

所有事件继承 `BotEvent`，包含 `Platform`、`SelfId` 和可选 `Raw`。通用事件包括：

| 类型 | 场景 |
| --- | --- |
| `MessageEvent` | 收到消息 |
| `MessageDeletedEvent` | 消息撤回或删除 |
| `MemberJoinedEvent` / `MemberLeftEvent` | 成员加入或离开 |
| `FriendRequestEvent` | 好友或私聊请求 |
| `GuildInviteEvent` | 机器人被邀请 |
| `BotOfflineEvent` | 机器人离线 |
| `PlatformEvent` | 平台特有事件；用 `Kind` 区分，`Raw` 承载平台 Model |

如何订阅与处理，参见[接收消息与事件](/plugin/routes-events)。
