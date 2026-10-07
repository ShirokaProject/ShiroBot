# 通用富消息、按钮与表情回应

这些契约位于 `ShiroBot.SDK.Models`、`ShiroBot.SDK.Adapter`，普通插件无需引用 QQ Model。能力按目标会话查询，表示适配器已经实现的映射；账号权限、平台审核和请求是否成功仍由实际调用决定。

## 发送与能力检查

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

## Markdown 与卡片

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

## 基础按钮与点击事件

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
            new("docs", "文档", new OpenUrlAction("https://shirobot.net/")),
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

`InstanceId`、`Platform`、`Channel`、`Kind`、`Raw` 沿用平台事件字段。同一个点击只发布一次事件；通用处理器和原来的平台处理器均可订阅，业务应避免重复执行。回调数据不代表授权，处理敏感操作时仍需核对点击用户权限。

```csharp
public interface IMessageInteractionService
{
    Task AcknowledgeAsync(InteractionEvent interaction,
        CancellationToken cancellationToken = default);
}
```

通过 `Context.GetAdapterExtension<IMessageInteractionService>()` 获取可选服务。确认只表示收到点击，不表示业务完成。官方适配器自动确认，重复确认复用同一次请求及其结果；不确定失败不会盲目重发。互动回复使用 `Context.Message.ReplyAsync(interaction, request)`；官方实现目前支持文本或 Markdown，来源事件必须由适配器接收过，其回复路由缓存有界，过旧事件可能无法再回复。

## 表情回应

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

## 当前适配器支持

| 能力 | QQ 官方群聊/私聊 | Milky |
| --- | --- | --- |
| 发送 Markdown | 原生；须为唯一正文 | 显式转文本；接收支持 MarkdownSegment |
| 链接按钮 | 原生 | 显式转标签和 URL |
| 回调按钮及点击 | 原生，自动确认 | 不支持 |
| 通用卡片 | 显式转 Markdown 或文本 | 显式转文本 |
| 通用 Reaction | 群聊/私聊暂未实现 | 群聊支持 Unicode 与平台表情 |
| 互动回复 | 文本或 Markdown | 不支持 |

官方通用按钮映射声明最多 5 行、每行 5 个按钮，按钮不能和媒体混发；通用请求最多一段媒体，普通文本最多 5000 字符。QQ 专属模板键盘、Ark、特殊按钮参数等仍保留 QQ Model 扩展。Typing、流式回复、通用审批和文件存储服务不在本轮实现范围。
