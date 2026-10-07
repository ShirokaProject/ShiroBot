# 通用 Model

跨适配器的消息、事件、用户和会话模型位于 `ShiroBot.SDK.Models`。插件优先使用这些类型；只有需要协议特有字段或操作时，才进入 [QQ Model](/plugin/qq-model)、[Discord Model](/plugin/discord-model) 等平台模型。

## 消息与会话

| 类型 | 作用 |
| --- | --- |
| `Channel` / `ChannelType` | 会话 ID 与类型；`Channel.Direct(id)`、`Channel.Group(id)` 可快速构造 |
| `MessageEvent` | 入站消息，包含 `MessageId`、`Channel`、`Sender`、`Segments`、`Timestamp` |
| `SentMessage` | 出站结果，包含 `MessageId`、可选时间、`IsSuccess` 与失败说明 `ErrorMessage`；发送失败时 ID 为空 |
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

所有事件继承 `BotEvent`，包含 `Platform`、`SelfId` 和可选 `Raw`；v0.9.7 起增加 `InstanceId`：

| 字段 | 含义 |
| --- | --- |
| `Platform` | 平台类型，例如 Milky 的 qq、官方 QQ 的 qq-official |
| `SelfId` | 平台机器人账号 ID，相同平台可以存在同账号的多个连接实例 |
| `InstanceId` | 宿主运行实例 ID，由宿主转发入站事件时填写；区分相同 Platform/SelfId 的实例 |

会话 ID、用户 ID 和消息 ID 都只有来源平台/实例内的意义。需要隔离状态时将 InstanceId 加入缓存键，例如 `(message.InstanceId, message.Channel.Id)`；主动发送时只有 Channel 无法推断实例，应显式选择上下文。用法见[调用 API](/plugin/apis#适配器实例与后台发送)。通用事件包括：

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

### QQ 群管理与事件

QQ Model ABI 1.0.0.0 统一使用字符串 ID，直接替换旧接口，引用 QQ Model 的插件和适配器需重新编译。
群信息、成员查询、禁言、踢人和入群审批统一通过 `GetAdapterExtension<IQGroupApi>()` 获取。
使用 `Capabilities` 检查适配器实现的操作；平台权限仍由 QQ 控制。
自动审批策略和白名单通过 `IQGroupApprovalStrategyApi` 探测。

两种适配器的入群申请均使用 `QEventKinds.GroupJoinRequest` 和 `QGroupJoinRequest`，
将原申请对象传给 `AcceptJoinRequestAsync` / `RejectJoinRequestAsync` 即可。
成员事件 Raw 使用 `QGroupMemberIncrease` / `QGroupMemberDecrease`。
QQPlatform 可用 `GlobalUserId` 保留额外的 `user_openid`。
官方事件需订阅 `GROUP_MEMBER_EVENT`（`1 << 24`）；管理接口仍需平台权限。

完整签名见 [QQ C# 接口参考](/plugin/qq-reference)，迁移说明见 [QQ 接口审阅](/plugin/qq-interface-review)。


通用 Markdown、基础按钮、卡片、互动事件及 Reaction 契约已加入 SDK，详见 [通用富消息与互动](./rich-messages.md)。
