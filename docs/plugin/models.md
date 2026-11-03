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

## 富消息、按钮与表情回应

这些契约位于 `ShiroBot.SDK.Models`、`ShiroBot.SDK.Adapter`。能力按目标会话查询，表示适配器已经实现的映射；账号权限、平台审核和请求是否成功仍由实际调用决定。

### 发送与能力检查

```csharp
MessageCapabilities GetMessageCapabilities(ChannelReference channel);
MessageSendAssessment AssessMessage(ChannelReference channel, OutgoingMessage message);
Task<SentMessage> SendMessageAsync(ChannelReference channel, OutgoingMessage message,
    CancellationToken cancellationToken = default);
Task<SentMessage> ReplyAsync(MessageEvent message, OutgoingMessage content,
    CancellationToken cancellationToken = default);
Task<SentMessage> ReplyAsync(InteractionEvent interaction, OutgoingMessage content,
    CancellationToken cancellationToken = default);
```

以上入口位于 `Context.Message`。`IMessageService` 同样提供以 `Channel` 为参数的能力检查和发送方法，使用当前实例。跨实例保存目标时使用 `ChannelReference`。

```csharp
var target = new ChannelReference("qq-main", Channel.Group("group-id"));
var request = new OutgoingMessage
{
    Segments = [new MarkdownSegment("**任务完成**")
    {
        PlainTextFallback = "任务完成"
    }],
    AllowedFallbacks = MessageFallbackOptions.MarkdownAsText
};
var assessment = Context.Message.AssessMessage(target, request);
if (assessment.IsSupported)
{
    var result = await Context.Message.SendMessageAsync(target, request);
    // 检查 result.IsSuccess；result.Transformations 记录实际使用的转换。
}
```

检查不会发送网络请求。`IsSupported` 表示请求可以按声明的规则准备；`IsNative` 表示没有降级。`Issues` 是不支持或参数错误的原因。发送结果的 `Reference` 只有成功且包含消息 ID 时可用。

`OutgoingMessage` 属性：

| 属性 | 类型 | 含义 |
| --- | --- | --- |
| Segments | IReadOnlyList&lt;MessageSegment&gt; | 消息正文，默认空数组 |
| Buttons | MessageButtonLayout? | 整条消息附带的按钮布局 |
| ReplyTo | MessageReference? | 引用来源消息，包含实例和会话 |
| ReplyToInteraction | InteractionReference? | 回复来源互动，与 ReplyTo 互斥 |
| AllowedFallbacks | MessageFallbackOptions | 默认 None，禁止降级 |

请求代表单条消息，超出单条限制会拒绝，不会自动拆分。`ReplyTo` 必须与发送目标的实例、会话相同。`ReplyAsync` 自动从来源事件选择实例。

能力字段包括 `NativeFeatures`，以及 `CanMixMarkdown`、`ButtonsRequireMarkdown`、`CanCombineButtonsWithMedia`；可选限制包括 `MaxButtonRows`、`MaxButtonsPerRow`、`MaxCallbackDataBytes`、`MaxMediaSegments`、`MaxTextLength`。限制为 null 表示适配器没有声明数值，并不表示平台没有限制。

### Markdown 与卡片

```csharp
public sealed record MarkdownSegment(string Content) : MessageSegment
{
    public string? PlainTextFallback { get; init; }
}
public sealed record CardField(string Name, string Value);
public sealed record CardSegment : MessageSegment
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<CardField> Fields { get; init; } = [];
}
```

原生 Markdown 表示使用目标的 Markdown 消息类型，具体语法效果受平台限制。`MarkdownAsText` 优先发送 `PlainTextFallback`，未提供时发送原始 Markdown 字符串，保留内容。

`CardAsMarkdown`、`CardAsText` 分别允许卡片转成 Markdown、文本；同时允许时优先 Markdown。转换保留标题、正文、字段及图片和跳转 URL。文本降级只显示图片链接。URL 必须是绝对 HTTP(S) 地址。

### 基础按钮与点击事件

```csharp
public abstract record ButtonAction;
public sealed record OpenUrlAction(string Url) : ButtonAction;
public sealed record CallbackAction(string Data) : ButtonAction;
public sealed record MessageButton(string Id, string Label, ButtonAction Action);
public sealed record MessageButtonRow(IReadOnlyList<MessageButton> Buttons);
public sealed record MessageButtonLayout(IReadOnlyList<MessageButtonRow> Rows);
```

```csharp
var request = new OutgoingMessage
{
    Segments = [new MarkdownSegment("请选择操作")],
    Buttons = new([
        new([
            new("docs", "文档", new OpenUrlAction("https://docs.shiroka.org/")),
            new("refresh", "刷新", new CallbackAction("refresh"))
        ])
    ])
};
```

按钮 ID 在布局内唯一。`LinkButtonsAsText` 允许链接按钮转成标签和 URL；包含回调按钮的布局不能降级或丢弃。官方适配器把带按钮的普通文本转成经过转义的 Markdown，标记 `TextToMarkdown`，这项转换不算降级。

点击事件类型为 `InteractionEvent`，继承 `PlatformEvent`，包含：

| 属性 | 类型 | 含义 |
| --- | --- | --- |
| InteractionId | string | 平台互动 ID |
| User | User | 点击用户 |
| ButtonId / Data / MessageId | string? | 按钮 ID、回调数据、来源消息 ID，可能未知 |
| Reference | InteractionReference | 实例、会话、互动 ID |
| Acknowledgement | InteractionAcknowledgement | Pending、Acknowledged、Failed 或 Unknown |
| IsAutomaticallyAcknowledged | bool | 适配器是否自动确认 |
| ExpiresAt | DateTimeOffset? | 平台未提供时为 null |

`InstanceId`、`Platform`、`Channel`、`Kind`、`Raw` 沿用平台事件字段。同一个点击只发布一次事件；通用处理器和平台事件处理器均可订阅，业务应避免重复执行。回调数据不代表授权，处理敏感操作时仍需核对点击用户权限。

```csharp
public interface IMessageInteractionService
{
    Task AcknowledgeAsync(InteractionEvent interaction,
        CancellationToken cancellationToken = default);
}
```

通过 `Context.GetAdapterExtension<IMessageInteractionService>()` 获取可选服务。确认只表示收到点击，不表示业务完成。官方适配器自动确认，重复确认复用同一次请求及其结果；不确定失败不会盲目重发。互动回复使用 `Context.Message.ReplyAsync(interaction, request)`；官方实现目前支持文本或 Markdown，来源事件必须由适配器接收过，其回复路由缓存有界，过旧事件可能无法再回复。

### 表情回应

```csharp
public interface IMessageReactionService
{
    ReactionCapabilities GetReactionCapabilities(Channel channel);
    Task SetReactionAsync(MessageReference message, ReactionEmoji emoji,
        bool isAdd = true, CancellationToken cancellationToken = default);
}
public abstract record ReactionEmoji;
public sealed record UnicodeReactionEmoji(string Value) : ReactionEmoji;
public sealed record PlatformReactionEmoji(string Id, string InstanceId) : ReactionEmoji
{
    public string? GuildId { get; init; }
}
```

```csharp
var reactions = Context.GetAdapterExtension<IMessageReactionService>();
if (reactions is not null)
    await reactions.SetReactionAsync(message.Reference, new UnicodeReactionEmoji("👍"));
```

可选能力为 `Unicode`、`PlatformEmoji`。查询使用当前选择的实例；操作使用消息引用中的来源实例，即使服务曾在另一个实例下取得也会正确路由。平台表情只能在其来源实例内使用，`GuildId` 可以进一步限定范围；不会把 QQ 表情 ID 当作 Unicode。

`MessageReactionEvent` 同样继承 `PlatformEvent`，包含 `MessageId`、`Emoji`、`IsAdded`、可空的 `User` 和 `Count`，以及完整 `Reference`；平台没有提供的数据保持未知。

### 当前适配器支持

| 能力 | QQ 官方群聊/私聊 | Milky |
| --- | --- | --- |
| 发送 Markdown | 原生；须为唯一正文 | 显式转文本；接收支持 MarkdownSegment |
| 链接按钮 | 原生 | 显式转标签和 URL |
| 回调按钮及点击 | 原生，自动确认 | 不支持 |
| 通用卡片 | 显式转 Markdown 或文本 | 显式转文本 |
| 通用 Reaction | 群聊/私聊暂未实现 | 群聊支持 Unicode 与平台表情 |
| 互动回复 | 文本或 Markdown | 不支持 |

官方通用按钮映射声明最多 5 行、每行 5 个按钮，按钮不能和媒体混发；通用请求最多一段媒体，普通文本最多 5000 字符。QQ 专属模板键盘、Ark、特殊按钮参数等仍保留 QQ Model 扩展。Typing、流式回复、通用审批和文件存储服务尚未提供通用接口。

## 事件

所有事件继承 `BotEvent`，包含 `Platform`，以及可选的 `SelfId`、`Raw` 和 `InstanceId`：

| 字段 | 含义 |
| --- | --- |
| `Platform` | 适配器声明的平台 ID，用于识别事件所属平台 |
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

QQ Model 的用户、群和消息 ID 使用字符串。
群信息、成员查询、禁言、踢人和入群审批统一通过 `GetAdapterExtension<IQGroupApi>()` 获取。
使用 `Capabilities` 检查适配器实现的操作；平台权限仍由 QQ 控制。
自动审批策略和白名单通过 `IQGroupApprovalStrategyApi` 探测。

两种适配器的入群申请均使用 `QEventKinds.GroupJoinRequest` 和 `QGroupJoinRequest`，
将原申请对象传给 `AcceptJoinRequestAsync` / `RejectJoinRequestAsync` 即可。
成员事件 Raw 使用 `QGroupMemberIncrease` / `QGroupMemberDecrease`。
QQPlatform 可用 `GlobalUserId` 保留额外的 `user_openid`。
官方事件需订阅 `GROUP_MEMBER_EVENT`（`1 << 24`）；管理接口仍需平台权限。

完整签名见 [QQ C# 接口参考](/plugin/qq-reference)，契约语义见 [QQ 接口审阅](/plugin/qq-interface-review)。
