# Telegram C# 类型参考

命名空间：`ShiroBot.Model.Telegram`。以下完整声明对应 Telegram Model ABI `1.1.0.0`。

`required` 属性必须初始化；`init` 属性在创建对象时设置；位置式 record 使用所列构造参数。继承类型同时拥有基类属性。

## 账号、传输与会话 ID

### TelegramAccountKind

适配器登录的账号类型。机器人与用户账号共用本 Model，但可用的扩展接口不同。

```csharp
public enum TelegramAccountKind
{
    /// <summary>BotFather 创建的机器人。</summary>
    Bot,

    /// <summary>用户自己的账号（MTProto 登录）。</summary>
    User
}
```

### TelegramTransport

适配器与 Telegram 通信的方式。

```csharp
public enum TelegramTransport
{
    /// <summary>HTTP Bot API（官方服务器或自建 Bot API Server）。</summary>
    BotApi,

    /// <summary>Telegram 原生协议 MTProto。</summary>
    Mtproto
}
```

### TelegramJsonFormat

`TelegramMessage.Json` 与 `TelegramEventPayload.Json` 的结构。

```csharp
public enum TelegramJsonFormat
{
    /// <summary>Bot API 对象（Message、Update 字段等），字段名与 Bot API 文档一致。</summary>
    BotApi,

    /// <summary>MTProto TL 对象的 JSON 表示，结构由适配器说明。</summary>
    Mtproto
}
```

### TelegramPeerKind

MTProto peer 的类型。

```csharp
public enum TelegramPeerKind
{
    User,

    /// <summary>普通群（chat）。</summary>
    BasicGroup,

    /// <summary>超级群组与频道（channel）。</summary>
    Channel
}
```

### TelegramChatIds

会话 ID 规则：本 Model 和 SDK 频道 ID 一律使用 Bot API 格式——用户为正数，普通群为负数， 超级群组和频道为 `-100…`。MTProto 适配器在内部与 peer ID 互相转换，插件保存的 ID 在两种适配器之间通用。

```csharp
public static class TelegramChatIds
{
    private const long ChannelOffset = 1_000_000_000_000;

    /// <summary>MTProto peer 转为 Bot API 格式的会话 ID。</summary>
    public static long ToChatId(TelegramPeerKind kind, long peerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peerId);
        return kind switch
        {
            TelegramPeerKind.User => peerId,
            TelegramPeerKind.BasicGroup => -peerId,
            TelegramPeerKind.Channel => -(ChannelOffset + peerId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>Bot API 格式的会话 ID 转为 MTProto peer。</summary>
    public static (TelegramPeerKind Kind, long PeerId) ToPeer(long chatId) => chatId switch
    {
        > 0 => (TelegramPeerKind.User, chatId),
        0 => throw new ArgumentOutOfRangeException(nameof(chatId), "会话 ID 不能为 0。"),
        <= -ChannelOffset - 1 => (TelegramPeerKind.Channel, -chatId - ChannelOffset),
        _ => (TelegramPeerKind.BasicGroup, -chatId)
    };
}
```

## 实体与枚举

### TelegramUser

Telegram 用户的协议无关表示。

```csharp
public sealed record TelegramUser
{
    public required long UserId { get; init; }
    public required string FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public bool IsBot { get; init; }

    /// <summary>用户客户端的 IETF 语言标签，如 <c>zh-hans</c>；只在用户与机器人交互时提供。</summary>
    public string? LanguageCode { get; init; }

    public bool IsPremium { get; init; }

    public string DisplayName => string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
```

### TelegramChatType

Telegram 会话类型。

```csharp
public enum TelegramChatType
{
    Private,
    Group,
    Supergroup,
    Channel
}
```

### TelegramChat

Telegram 会话。查询结果（`ITelegramChatApi.GetChatAsync`）会填充资料字段，事件中通常只有基础字段。

```csharp
public sealed record TelegramChat
{
    public required long ChatId { get; init; }
    public required TelegramChatType Type { get; init; }

    /// <summary>群、超级群组和频道的标题。</summary>
    public string? Title { get; init; }

    public string? Username { get; init; }

    /// <summary>私聊对方的名字。</summary>
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>超级群组已启用话题。</summary>
    public bool IsForum { get; init; }

    /// <summary>频道的私信会话。</summary>
    public bool IsDirectMessages { get; init; }

    public string? Description { get; init; }

    /// <summary>私聊对方的简介。</summary>
    public string? Bio { get; init; }

    /// <summary>主邀请链接；机器人需要相应管理员权限才能看到。</summary>
    public string? InviteLink { get; init; }

    /// <summary>群组成员的默认权限。</summary>
    public TelegramChatPermissions? Permissions { get; init; }

    /// <summary>慢速模式间隔。</summary>
    public TimeSpan? SlowModeDelay { get; init; }

    /// <summary>频道的讨论组，或讨论组所属的频道。</summary>
    public long? LinkedChatId { get; init; }

    public string DisplayName => Title ?? string.Join(" ",
        new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
```

### TelegramMemberStatus

成员在会话中的状态。

```csharp
public enum TelegramMemberStatus
{
    /// <summary>群主（Bot API 的 <c>creator</c>）。</summary>
    Owner,
    Administrator,
    Member,

    /// <summary>受限成员；<see cref="TelegramChatMember.IsMember"/> 表示其是否仍在会话中。</summary>
    Restricted,
    Left,

    /// <summary>已封禁（Bot API 的 <c>kicked</c>）。</summary>
    Banned
}
```

### TelegramChatMember

会话成员及其权限。

```csharp
public sealed record TelegramChatMember
{
    public required TelegramUser User { get; init; }
    public required TelegramMemberStatus Status { get; init; }

    /// <summary>群主或管理员的自定义头衔。</summary>
    public string? CustomTitle { get; init; }

    public bool IsAnonymous { get; init; }

    /// <summary>受限成员是否仍在会话中。</summary>
    public bool IsMember { get; init; }

    /// <summary>限制或封禁的结束时间；null 表示永久。</summary>
    public DateTimeOffset? UntilDate { get; init; }

    /// <summary>受限成员的权限。</summary>
    public TelegramChatPermissions? Permissions { get; init; }

    /// <summary>管理员权限；群主拥有全部权限，此处为 null。</summary>
    public TelegramAdminRights? AdminRights { get; init; }

    /// <summary>机器人能否修改该管理员的权限。</summary>
    public bool CanBeEdited { get; init; }
}
```

### TelegramChatPermissions

成员权限。null 表示不修改（设置时）或平台未返回（读取时）。

```csharp
public sealed record TelegramChatPermissions
{
    public bool? CanSendMessages { get; init; }
    public bool? CanSendAudios { get; init; }
    public bool? CanSendDocuments { get; init; }
    public bool? CanSendPhotos { get; init; }
    public bool? CanSendVideos { get; init; }
    public bool? CanSendVideoNotes { get; init; }
    public bool? CanSendVoiceNotes { get; init; }
    public bool? CanSendPolls { get; init; }

    /// <summary>贴纸、GIF、游戏和 inline 机器人消息。</summary>
    public bool? CanSendOtherMessages { get; init; }

    public bool? CanAddWebPagePreviews { get; init; }
    public bool? CanReactToMessages { get; init; }
    public bool? CanChangeInfo { get; init; }
    public bool? CanInviteUsers { get; init; }
    public bool? CanPinMessages { get; init; }
    public bool? CanManageTopics { get; init; }

    /// <summary>禁止发送任何内容。</summary>
    public static TelegramChatPermissions ReadOnly { get; } = new()
    {
        CanSendMessages = false, CanSendAudios = false, CanSendDocuments = false, CanSendPhotos = false,
        CanSendVideos = false, CanSendVideoNotes = false, CanSendVoiceNotes = false, CanSendPolls = false,
        CanSendOtherMessages = false, CanAddWebPagePreviews = false, CanReactToMessages = false
    };
}
```

### TelegramAdminRights

管理员权限。提升成员时，未设置的项视为 false。

```csharp
public sealed record TelegramAdminRights
{
    public bool IsAnonymous { get; init; }
    public bool CanManageChat { get; init; }
    public bool CanDeleteMessages { get; init; }
    public bool CanManageVideoChats { get; init; }
    public bool CanRestrictMembers { get; init; }
    public bool CanPromoteMembers { get; init; }
    public bool CanChangeInfo { get; init; }
    public bool CanInviteUsers { get; init; }
    public bool CanPostStories { get; init; }
    public bool CanEditStories { get; init; }
    public bool CanDeleteStories { get; init; }

    /// <summary>仅频道。</summary>
    public bool CanPostMessages { get; init; }

    /// <summary>仅频道。</summary>
    public bool CanEditMessages { get; init; }

    public bool CanPinMessages { get; init; }

    /// <summary>仅超级群组。</summary>
    public bool CanManageTopics { get; init; }

    public bool CanManageDirectMessages { get; init; }
}
```

### TelegramForumTopic

超级群组的话题。SDK 频道 ID 为 `chatId:ThreadId`。

```csharp
public sealed record TelegramForumTopic
{
    public required long ThreadId { get; init; }
    public required string Name { get; init; }

    /// <summary>RGB 图标颜色。</summary>
    public int IconColor { get; init; }

    public string? IconCustomEmojiId { get; init; }
}
```

### TelegramInviteLink

邀请链接。

```csharp
public sealed record TelegramInviteLink
{
    public required string Link { get; init; }
    public string? Name { get; init; }
    public TelegramUser? Creator { get; init; }

    /// <summary>通过该链接加入需要管理员审批，产生 <see cref="TelegramEventKinds.ChatJoinRequest"/> 事件。</summary>
    public bool CreatesJoinRequest { get; init; }

    public bool IsPrimary { get; init; }
    public bool IsRevoked { get; init; }
    public DateTimeOffset? ExpireDate { get; init; }
    public int? MemberLimit { get; init; }
    public int? PendingJoinRequestCount { get; init; }
}
```

### TelegramInviteLinkOptions

创建或修改邀请链接的选项。

```csharp
public sealed record TelegramInviteLinkOptions
{
    public string? Name { get; init; }
    public DateTimeOffset? ExpireDate { get; init; }

    /// <summary>1–99999；与 <see cref="CreatesJoinRequest"/> 互斥。</summary>
    public int? MemberLimit { get; init; }

    public bool CreatesJoinRequest { get; init; }
}
```

### TelegramBotCommand

机器人命令，显示在客户端的命令菜单中。

```csharp
public sealed record TelegramBotCommand(string Command, string Description);
```

### TelegramCommandScopeKind

```csharp
public enum TelegramCommandScopeKind
{
    Default,
    AllPrivateChats,
    AllGroupChats,
    AllChatAdministrators,

    /// <summary>需要 <see cref="TelegramCommandScope.ChatId"/>。</summary>
    Chat,

    /// <summary>需要 <see cref="TelegramCommandScope.ChatId"/>。</summary>
    ChatAdministrators,

    /// <summary>需要 <see cref="TelegramCommandScope.ChatId"/> 和 <see cref="TelegramCommandScope.UserId"/>。</summary>
    ChatMember
}
```

### TelegramCommandScope

命令的生效范围。

```csharp
public sealed record TelegramCommandScope(TelegramCommandScopeKind Kind)
{
    /// <summary>会话 ID，数字或 <c>@username</c>。</summary>
    public string? ChatId { get; init; }

    public string? UserId { get; init; }

    public static TelegramCommandScope Default { get; } = new(TelegramCommandScopeKind.Default);
}
```

## 消息内容、键盘与入站内容

### TelegramParseMode

正文的格式。

```csharp
public enum TelegramParseMode
{
    /// <summary>纯文本，原样发送。</summary>
    None,

    /// <summary>Telegram MarkdownV2，特殊字符需要转义。</summary>
    MarkdownV2,

    Html
}
```

### TelegramSendOptions

对应主消息段：[`QuoteSegment`](../segments#quotesegment)

Telegram 原生消息的公共发送选项。目标会话使用 SDK `Channel`，话题频道 ID 为 `chatId:threadId`。

```csharp
public sealed record TelegramSendOptions
{
    /// <summary>静默发送，接收方没有提示音。</summary>
    public bool DisableNotification { get; init; }

    /// <summary>禁止转发和保存。</summary>
    public bool ProtectContent { get; init; }

    /// <summary>引用回复的消息 ID。</summary>
    public string? ReplyToMessageId { get; init; }

    /// <summary>被引用消息已删除时仍然发送。</summary>
    public bool AllowSendingWithoutReply { get; init; } = true;

    public TelegramReplyMarkup? ReplyMarkup { get; init; }

    /// <summary>消息特效 ID，仅私聊。</summary>
    public string? MessageEffectId { get; init; }

    /// <summary>代表企业账号发送。</summary>
    public string? BusinessConnectionId { get; init; }

    /// <summary>为超过免费额度的广播支付 Telegram Stars。</summary>
    public bool AllowPaidBroadcast { get; init; }
}
```

### TelegramReplyMarkup

消息下方或输入框中的键盘。

```csharp
public abstract record TelegramReplyMarkup;
```

### TelegramInlineKeyboard

对应主消息段：[`MessageButton`](../segments#messagebutton)

消息下方的内联键盘，最多 100 个按钮。

```csharp
public sealed record TelegramInlineKeyboard(IReadOnlyList<IReadOnlyList<TelegramInlineButton>> Rows) : TelegramReplyMarkup;
```

### TelegramInlineButton

对应主消息段：[`MessageButton`](../segments#messagebutton)

内联按钮，必须且只能设置一种动作。

```csharp
public sealed record TelegramInlineButton(string Text)
{
    public string? Url { get; init; }

    /// <summary>1–64 字节，点击后产生 SDK <see cref="InteractionEvent"/>，<c>Data</c> 即此值。</summary>
    public string? CallbackData { get; init; }

    /// <summary>打开 Mini App 的 HTTPS 地址。</summary>
    public string? WebAppUrl { get; init; }

    /// <summary>让用户选择会话并在其中发起 inline 查询；空字符串只插入机器人用户名。</summary>
    public string? SwitchInlineQuery { get; init; }

    /// <summary>在当前会话发起 inline 查询。</summary>
    public string? SwitchInlineQueryCurrentChat { get; init; }

    /// <summary>点击后复制到剪贴板的文本。</summary>
    public string? CopyText { get; init; }

    /// <summary>支付按钮，必须是发票消息的第一个按钮。</summary>
    public bool Pay { get; init; }

    public static TelegramInlineButton WithUrl(string text, string url) => new(text) { Url = url };

    public static TelegramInlineButton WithCallback(string text, string data) => new(text) { CallbackData = data };
}
```

### TelegramReplyKeyboard

替换用户输入框的自定义键盘。

```csharp
public sealed record TelegramReplyKeyboard(IReadOnlyList<IReadOnlyList<TelegramKeyboardButton>> Rows) : TelegramReplyMarkup
{
    /// <summary>键盘收起后仍显示切换按钮。</summary>
    public bool IsPersistent { get; init; }

    /// <summary>按按钮数量缩小键盘高度。</summary>
    public bool ResizeKeyboard { get; init; } = true;

    /// <summary>用户按下按钮后自动收起。</summary>
    public bool OneTimeKeyboard { get; init; }

    public string? InputFieldPlaceholder { get; init; }

    /// <summary>只对被提及或被回复的用户显示。</summary>
    public bool Selective { get; init; }
}
```

### TelegramKeyboardButton

自定义键盘按钮；没有请求项时按下即发送 `Text`。

```csharp
public sealed record TelegramKeyboardButton(string Text)
{
    /// <summary>发送用户的电话号码，仅私聊。</summary>
    public bool RequestContact { get; init; }

    /// <summary>发送用户的位置，仅私聊。</summary>
    public bool RequestLocation { get; init; }

    /// <summary>让用户创建投票并发送。</summary>
    public bool RequestPoll { get; init; }

    /// <summary>限制可创建的投票类型；null 不限制。</summary>
    public TelegramPollType? RequestPollType { get; init; }

    /// <summary>打开 Mini App 的 HTTPS 地址，仅私聊。</summary>
    public string? WebAppUrl { get; init; }
}
```

### TelegramRemoveKeyboard

移除自定义键盘。

```csharp
public sealed record TelegramRemoveKeyboard : TelegramReplyMarkup
{
    public bool Selective { get; init; }
}
```

### TelegramForceReply

让客户端直接进入回复本消息的状态。

```csharp
public sealed record TelegramForceReply : TelegramReplyMarkup
{
    public string? InputFieldPlaceholder { get; init; }
    public bool Selective { get; init; }
}
```

### TelegramOutgoingContent

`ITelegramMessageApi.SendAsync` 可发送的 Telegram 原生内容。普通文本和媒体也可使用 SDK 消息服务。

```csharp
public abstract record TelegramOutgoingContent;
```

### TelegramTextContent

对应主消息段：[`TextSegment`](../segments#textsegment)、[`MarkdownSegment`](../segments#markdownsegment)

格式化文本。

```csharp
public sealed record TelegramTextContent(string Text) : TelegramOutgoingContent
{
    public TelegramParseMode ParseMode { get; init; }

    /// <summary>不生成链接预览。</summary>
    public bool DisableLinkPreview { get; init; }
}
```

### TelegramPollType

```csharp
public enum TelegramPollType
{
    Regular,
    Quiz
}
```

### TelegramPollContent

对应主消息段：[`RawSegment`](../segments#rawsegment)

投票或测验，2–12 个选项。

```csharp
public sealed record TelegramPollContent(string Question, IReadOnlyList<string> Options) : TelegramOutgoingContent
{
    public TelegramPollType Type { get; init; }
    public bool IsAnonymous { get; init; } = true;
    public bool AllowsMultipleAnswers { get; init; }

    /// <summary>测验的正确选项，从 0 开始。</summary>
    public IReadOnlyList<int>? CorrectOptionIds { get; init; }

    /// <summary>答错或点击灯泡后显示的解释，0–200 个字符。</summary>
    public string? Explanation { get; init; }

    /// <summary>5–600 秒后自动结束。</summary>
    public TimeSpan? OpenPeriod { get; init; }

    public DateTimeOffset? CloseDate { get; init; }
}
```

### TelegramLocationContent

对应主消息段：[`RawSegment`](../segments#rawsegment)

位置；设置 `LivePeriod` 时为实时位置。

```csharp
public sealed record TelegramLocationContent(double Latitude, double Longitude) : TelegramOutgoingContent
{
    /// <summary>误差半径，0–1500 米。</summary>
    public double? HorizontalAccuracy { get; init; }

    /// <summary>实时位置的更新时长，60 秒至 86400 秒。</summary>
    public TimeSpan? LivePeriod { get; init; }

    /// <summary>移动方向，1–360 度，仅实时位置。</summary>
    public int? Heading { get; init; }
}
```

### TelegramVenueContent

对应主消息段：[`RawSegment`](../segments#rawsegment)

地点。

```csharp
public sealed record TelegramVenueContent(double Latitude, double Longitude, string Title, string Address) : TelegramOutgoingContent
{
    public string? FoursquareId { get; init; }
    public string? GooglePlaceId { get; init; }
}
```

### TelegramContactContent

对应主消息段：[`RawSegment`](../segments#rawsegment)

联系人名片。

```csharp
public sealed record TelegramContactContent(string PhoneNumber, string FirstName) : TelegramOutgoingContent
{
    public string? LastName { get; init; }

    /// <summary>vCard 格式的附加信息，0–2048 字节。</summary>
    public string? VCard { get; init; }
}
```

### TelegramDiceContent

对应主消息段：[`RawSegment`](../segments#rawsegment)

随机动画表情，Telegram 决定点数。

```csharp
public sealed record TelegramDiceContent(string Emoji = "🎲") : TelegramOutgoingContent;
```

### TelegramStickerContent

对应主消息段：[`EmojiSegment`](../segments#emojisegment)

贴纸。

```csharp
public sealed record TelegramStickerContent(string Sticker) : TelegramOutgoingContent
{
    /// <summary>新上传贴纸对应的表情。</summary>
    public string? Emoji { get; init; }
}
```

### TelegramMediaKind

```csharp
public enum TelegramMediaKind
{
    Photo,
    Video,
    Audio,
    Document
}
```

### TelegramMediaItem

对应主消息段：[`ImageSegment`](../segments#imagesegment)、[`AudioSegment`](../segments#audiosegment)、[`VideoSegment`](../segments#videosegment)、[`FileSegment`](../segments#filesegment)

相册中的一项。

```csharp
public sealed record TelegramMediaItem(TelegramMediaKind Kind, string Uri)
{
    public string? Caption { get; init; }
    public TelegramParseMode ParseMode { get; init; }
    public string? FileName { get; init; }

    /// <summary>以遮罩显示，点击后才可见；仅图片和视频。</summary>
    public bool HasSpoiler { get; init; }
}
```

### TelegramMediaGroupContent

相册，2–10 项，作为一组消息发送。图片和视频可以混排；音频只能与音频、文件只能与文件组成相册。 相册不能附带键盘。

```csharp
public sealed record TelegramMediaGroupContent(IReadOnlyList<TelegramMediaItem> Items) : TelegramOutgoingContent;
```

### TelegramLabeledPrice

价格项，金额以货币最小单位计；Telegram Stars（XTR）以 Star 计。

```csharp
public sealed record TelegramLabeledPrice(string Label, long Amount);
```

### TelegramInvoiceContent

对应主消息段：[`RawSegment`](../segments#rawsegment)

发票。使用 Telegram Stars 时 `Currency` 为 XTR，且不需要支付服务商令牌。

```csharp
public sealed record TelegramInvoiceContent(string Title, string Description, string Payload, string Currency,
    IReadOnlyList<TelegramLabeledPrice> Prices) : TelegramOutgoingContent
{
    /// <summary>支付服务商令牌；Telegram Stars 留空。</summary>
    public string? ProviderToken { get; init; }

    public string? PhotoUrl { get; init; }
    public bool NeedName { get; init; }
    public bool NeedPhoneNumber { get; init; }
    public bool NeedEmail { get; init; }
    public bool NeedShippingAddress { get; init; }

    /// <summary>最终价格取决于收货方式，会产生 <see cref="TelegramEventKinds.ShippingQuery"/> 事件。</summary>
    public bool IsFlexible { get; init; }
}
```

### TelegramSentMessage

发送后的消息。相册返回每一条。

```csharp
public sealed record TelegramSentMessage
{
    public required string MessageId { get; init; }

    /// <summary>所在 SDK 频道。</summary>
    public required Channel Channel { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>相册的分组 ID。</summary>
    public string? MediaGroupId { get; init; }
}
```

### TelegramChatAction

正在进行的动作，显示在会话标题下，约 5 秒后或发送消息时消失。

```csharp
public enum TelegramChatAction
{
    Typing,
    UploadPhoto,
    RecordVideo,
    UploadVideo,
    RecordVoice,
    UploadVoice,
    UploadDocument,
    ChooseSticker,
    FindLocation,
    RecordVideoNote,
    UploadVideoNote
}
```

### TelegramCallbackAnswer

回答按钮回调时的提示。

```csharp
public sealed record TelegramCallbackAnswer
{
    /// <summary>0–200 个字符的提示文本。</summary>
    public string? Text { get; init; }

    /// <summary>以弹窗而不是顶部通知显示。</summary>
    public bool ShowAlert { get; init; }

    /// <summary>打开的地址，例如游戏或 <c>t.me/机器人?start=参数</c>。</summary>
    public string? Url { get; init; }

    /// <summary>客户端缓存回答的时长。</summary>
    public TimeSpan? CacheTime { get; init; }
}
```

### TelegramInlineResult

inline 查询的一项结果。

```csharp
public abstract record TelegramInlineResult(string Id)
{
    public TelegramInlineKeyboard? ReplyMarkup { get; init; }
}
```

### TelegramInlineArticle

选择后发送一段文本。

```csharp
public sealed record TelegramInlineArticle(string Id, string Title, string MessageText) : TelegramInlineResult(Id)
{
    public TelegramParseMode ParseMode { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? ThumbnailUrl { get; init; }
}
```

### TelegramInlinePhoto

选择后发送图片。

```csharp
public sealed record TelegramInlinePhoto(string Id, string Photo) : TelegramInlineResult(Id)
{
    /// <summary>使用地址时必填。</summary>
    public string? ThumbnailUrl { get; init; }

    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Caption { get; init; }
    public TelegramParseMode ParseMode { get; init; }
}
```

### TelegramInlineSticker

选择后发送已有的贴纸。

```csharp
public sealed record TelegramInlineSticker(string Id, string StickerFileId) : TelegramInlineResult(Id);
```

### TelegramInlineAnswerOptions

回答 inline 查询的选项。

```csharp
public sealed record TelegramInlineAnswerOptions
{
    /// <summary>服务器缓存结果的时长，默认 300 秒。</summary>
    public TimeSpan? CacheTime { get; init; }

    /// <summary>只为发起查询的用户缓存。</summary>
    public bool IsPersonal { get; init; }

    /// <summary>用户滚动到底部时下一次查询携带的 offset；空字符串表示没有更多结果。</summary>
    public string? NextOffset { get; init; }
}
```

### TelegramMessage

Telegram 消息的原生资料，放在 `MessageEvent.Raw`。`Json` 保留原始消息， 结构由 `JsonFormat` 说明，可按需读取本模型未列出的字段。

```csharp
public sealed record TelegramMessage
{
    public required long MessageId { get; init; }
    public required TelegramChat Chat { get; init; }

    /// <summary>话题 ID。</summary>
    public long? ThreadId { get; init; }

    /// <summary>发送者；频道消息和匿名管理员没有。</summary>
    public TelegramUser? From { get; init; }

    /// <summary>以频道或群身份发送时的会话。</summary>
    public TelegramChat? SenderChat { get; init; }

    public DateTimeOffset Date { get; init; }
    public DateTimeOffset? EditDate { get; init; }

    /// <summary>同一相册的消息共享该 ID。</summary>
    public string? MediaGroupId { get; init; }

    /// <summary>转发消息的来源。</summary>
    public TelegramForwardOrigin? ForwardOrigin { get; init; }

    /// <summary>频道消息自动转发到讨论组。</summary>
    public bool IsAutomaticForward { get; init; }

    /// <summary>通过 inline 模式发送时的机器人。</summary>
    public TelegramUser? ViaBot { get; init; }

    /// <summary>频道签名或匿名管理员的头衔。</summary>
    public string? AuthorSignature { get; init; }

    public string? BusinessConnectionId { get; init; }

    /// <summary>原始消息对象。</summary>
    public JsonElement Json { get; init; }

    /// <summary><see cref="Json"/> 的结构：机器人适配器为 Bot API Message。</summary>
    public TelegramJsonFormat JsonFormat { get; init; }
}
```

### TelegramForwardOriginKind

```csharp
public enum TelegramForwardOriginKind
{
    User,

    /// <summary>隐藏了账号的用户，只有 <see cref="TelegramForwardOrigin.SenderName"/>。</summary>
    HiddenUser,
    Chat,
    Channel
}
```

### TelegramForwardOrigin

转发来源。

```csharp
public sealed record TelegramForwardOrigin
{
    public required TelegramForwardOriginKind Kind { get; init; }
    public DateTimeOffset Date { get; init; }
    public TelegramUser? User { get; init; }
    public string? SenderName { get; init; }
    public TelegramChat? Chat { get; init; }

    /// <summary>频道来源的原消息 ID。</summary>
    public long? MessageId { get; init; }

    public string? AuthorSignature { get; init; }
}
```

### TelegramSegmentKinds

消息中没有通用 SDK 段的 Telegram 内容，以 `RawSegment` 上报： `Platform` 为 telegram，`Kind` 为下列常量，`Payload` 为对应记录。 其他未建模内容的 `Kind` 为 Bot API 字段名，`Payload` 为 `JsonElement`。

```csharp
public static class TelegramSegmentKinds
{
    /// <summary><see cref="TelegramLocation"/>。</summary>
    public const string Location = "location";

    /// <summary><see cref="TelegramVenue"/>。</summary>
    public const string Venue = "venue";

    /// <summary><see cref="TelegramContact"/>。</summary>
    public const string Contact = "contact";

    /// <summary><see cref="TelegramDice"/>。</summary>
    public const string Dice = "dice";

    /// <summary><see cref="TelegramPoll"/>。</summary>
    public const string Poll = "poll";

    /// <summary><see cref="TelegramSuccessfulPayment"/>。</summary>
    public const string SuccessfulPayment = "successful_payment";

    /// <summary><see cref="TelegramFormattedText"/>：带格式的文本或说明，与文本段一同出现。</summary>
    public const string FormattedText = "formatted_text";
}
```

### TelegramLocation

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
public sealed record TelegramLocation(double Latitude, double Longitude)
{
    public double? HorizontalAccuracy { get; init; }
    public TimeSpan? LivePeriod { get; init; }
    public int? Heading { get; init; }
}
```

### TelegramVenue

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
public sealed record TelegramVenue(TelegramLocation Location, string Title, string Address)
{
    public string? FoursquareId { get; init; }
    public string? GooglePlaceId { get; init; }
}
```

### TelegramContact

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
public sealed record TelegramContact(string PhoneNumber, string FirstName)
{
    public string? LastName { get; init; }

    /// <summary>联系人是 Telegram 用户时的 ID。</summary>
    public long? UserId { get; init; }

    public string? VCard { get; init; }
}
```

### TelegramDice

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
public sealed record TelegramDice(string Emoji, int Value);
```

### TelegramPollOption

```csharp
public sealed record TelegramPollOption(string Text, int VoterCount);
```

### TelegramPoll

对应主消息段：[`RawSegment`](../segments#rawsegment)

投票的当前状态。

```csharp
public sealed record TelegramPoll
{
    public required string PollId { get; init; }
    public required string Question { get; init; }
    public IReadOnlyList<TelegramPollOption> Options { get; init; } = [];
    public int TotalVoterCount { get; init; }
    public bool IsClosed { get; init; }
    public bool IsAnonymous { get; init; }
    public TelegramPollType Type { get; init; }
    public bool AllowsMultipleAnswers { get; init; }

    /// <summary>测验的正确选项；机器人只能看到自己发送的或已结束的测验的答案。</summary>
    public IReadOnlyList<int>? CorrectOptionIds { get; init; }

    public string? Explanation { get; init; }
    public DateTimeOffset? CloseDate { get; init; }
}
```

### TelegramSuccessfulPayment

对应主消息段：[`RawSegment`](../segments#rawsegment)

成功付款的服务消息。

```csharp
public sealed record TelegramSuccessfulPayment
{
    public required string Currency { get; init; }
    public long TotalAmount { get; init; }
    public required string InvoicePayload { get; init; }

    /// <summary>退款时使用（<see cref="ITelegramPaymentApi.RefundStarPaymentAsync"/>）。</summary>
    public required string TelegramPaymentChargeId { get; init; }

    public string? ProviderPaymentChargeId { get; init; }
    public string? ShippingOptionId { get; init; }

    /// <summary>订阅的到期时间。</summary>
    public DateTimeOffset? SubscriptionExpirationDate { get; init; }
}
```

### TelegramTextEntity

Telegram 格式实体（粗体、剧透、代码、链接、自定义表情等）。偏移和长度以 UTF-16 代码单元计。

```csharp
public sealed record TelegramTextEntity(string Type, int Offset, int Length)
{
    public string? Url { get; init; }
    public TelegramUser? User { get; init; }
    public string? Language { get; init; }
    public string? CustomEmojiId { get; init; }
}
```

### TelegramFormattedText

对应主消息段：[`TextSegment`](../segments#textsegment)、[`MarkdownSegment`](../segments#markdownsegment)、[`RawSegment`](../segments#rawsegment)

消息文本或说明及其格式实体；SDK 文本段只保留纯文本。

```csharp
public sealed record TelegramFormattedText(string Text, IReadOnlyList<TelegramTextEntity> Entities);
```

## 事件与事件 Kind

### TelegramEventKinds

Telegram 特有事件。适配器以 `PlatformEvent` 上报：`Kind` 为下列常量，`Raw` 为对应负载。 其他没有建模的 Update 以 Update 字段名为 `Kind`，`Raw` 为 `JsonElement`。 消息、按钮回调、成员加入/离开和 Reaction 照常映射为 SDK 事件。

```csharp
public static class TelegramEventKinds
{
    /// <summary>消息或频道消息被编辑；<see cref="TelegramMessageEdited"/>。</summary>
    public const string MessageEdited = "message_edited";

    /// <summary>其他成员的状态变化（需要管理员权限并订阅 chat_member）；<see cref="TelegramChatMemberUpdated"/>。</summary>
    public const string ChatMemberUpdated = "chat_member_updated";

    /// <summary>当前账号自己在会话中的状态变化：被拉入、移出、提升、私聊中被屏蔽；<see cref="TelegramChatMemberUpdated"/>。</summary>
    public const string MyChatMemberUpdated = "my_chat_member_updated";

    /// <summary>入群申请（仅机器人）；<see cref="TelegramChatJoinRequest"/>。</summary>
    public const string ChatJoinRequest = "chat_join_request";

    /// <summary>inline 查询（仅机器人）；<see cref="TelegramInlineQuery"/>。</summary>
    public const string InlineQuery = "inline_query";

    /// <summary>用户选择了 inline 结果（仅机器人，需在 BotFather 开启反馈）；<see cref="TelegramChosenInlineResult"/>。</summary>
    public const string ChosenInlineResult = "chosen_inline_result";

    /// <summary>投票状态变化；<see cref="TelegramPollUpdated"/>。</summary>
    public const string Poll = "poll";

    /// <summary>非匿名投票中用户的选择（仅机器人发起的投票）；<see cref="TelegramPollAnswer"/>。</summary>
    public const string PollAnswer = "poll_answer";

    /// <summary>付款前确认（仅机器人），需在 10 秒内回答；<see cref="TelegramPreCheckoutQuery"/>。</summary>
    public const string PreCheckoutQuery = "pre_checkout_query";

    /// <summary>灵活价格发票的收货地址（仅机器人）；<see cref="TelegramShippingQuery"/>。</summary>
    public const string ShippingQuery = "shipping_query";

    /// <summary>会话获得或续期助力（仅机器人）；<see cref="TelegramChatBoost"/>。</summary>
    public const string ChatBoost = "chat_boost";

    /// <summary>会话失去助力（仅机器人）；<see cref="TelegramChatBoost"/>，<c>IsRemoved</c> 为 true。</summary>
    public const string ChatBoostRemoved = "removed_chat_boost";
}
```

### TelegramEventPayload

Telegram 事件负载的公共字段。

```csharp
public abstract record TelegramEventPayload
{
    /// <summary>Bot API Update ID；MTProto 适配器没有对应序号时为 0。</summary>
    public long UpdateId { get; init; }

    public DateTimeOffset Time { get; init; }

    /// <summary>原始对象，结构由 <see cref="JsonFormat"/> 说明。</summary>
    public JsonElement Json { get; init; }

    public TelegramJsonFormat JsonFormat { get; init; }
}
```

### TelegramMessageEdited

编辑后的消息，内容按 SDK 消息段映射。

```csharp
public sealed record TelegramMessageEdited : TelegramEventPayload
{
    public required Channel Channel { get; init; }
    public required string MessageId { get; init; }
    public required TelegramMessage Message { get; init; }
    public IReadOnlyList<MessageSegment> Segments { get; init; } = [];
    public bool IsChannelPost { get; init; }
}
```

### TelegramChatMemberUpdated

```csharp
public sealed record TelegramChatMemberUpdated : TelegramEventPayload
{
    public required TelegramChat Chat { get; init; }

    /// <summary>执行变更的用户。</summary>
    public required TelegramUser Actor { get; init; }

    public required TelegramChatMember OldMember { get; init; }
    public required TelegramChatMember NewMember { get; init; }

    /// <summary>用户通过的邀请链接。</summary>
    public TelegramInviteLink? InviteLink { get; init; }

    public bool ViaJoinRequest { get; init; }
}
```

### TelegramChatJoinRequest

入群申请。用 `ITelegramChatApi.ApproveJoinRequestAsync` 或 `ITelegramChatApi.DeclineJoinRequestAsync` 处理。

```csharp
public sealed record TelegramChatJoinRequest : TelegramEventPayload
{
    public required TelegramChat Chat { get; init; }
    public required TelegramUser User { get; init; }

    /// <summary>可在 5 分钟内向申请人发私信的会话 ID。</summary>
    public long UserChatId { get; init; }

    public string? Bio { get; init; }
    public TelegramInviteLink? InviteLink { get; init; }
}
```

### TelegramInlineQuery

inline 查询。用 `ITelegramInlineApi.AnswerInlineQueryAsync` 回答。

```csharp
public sealed record TelegramInlineQuery : TelegramEventPayload
{
    public required string QueryId { get; init; }
    public required TelegramUser From { get; init; }
    public required string Query { get; init; }

    /// <summary>上次回答的 NextOffset。</summary>
    public string Offset { get; init; } = string.Empty;

    /// <summary>发起查询的会话类型；Telegram 不总是提供。</summary>
    public TelegramChatType? ChatType { get; init; }

    public TelegramLocation? Location { get; init; }
}
```

### TelegramChosenInlineResult

```csharp
public sealed record TelegramChosenInlineResult : TelegramEventPayload
{
    public required string ResultId { get; init; }
    public required TelegramUser From { get; init; }
    public required string Query { get; init; }

    /// <summary>结果带内联键盘时提供，可用于编辑该 inline 消息。</summary>
    public string? InlineMessageId { get; init; }
}
```

### TelegramPollUpdated

投票状态变化事件的负载。

```csharp
public sealed record TelegramPollUpdated : TelegramEventPayload
{
    public required TelegramPoll Poll { get; init; }
}
```

### TelegramPollAnswer

```csharp
public sealed record TelegramPollAnswer : TelegramEventPayload
{
    public required string PollId { get; init; }

    /// <summary>投票的用户；匿名身份投票时为 null，见 <see cref="VoterChat"/>。</summary>
    public TelegramUser? User { get; init; }

    public TelegramChat? VoterChat { get; init; }

    /// <summary>选择的选项，从 0 开始；撤回投票时为空。</summary>
    public IReadOnlyList<int> OptionIds { get; init; } = [];
}
```

### TelegramPreCheckoutQuery

付款前确认。用 `ITelegramPaymentApi.AnswerPreCheckoutQueryAsync` 回答。

```csharp
public sealed record TelegramPreCheckoutQuery : TelegramEventPayload
{
    public required string QueryId { get; init; }
    public required TelegramUser From { get; init; }
    public required string Currency { get; init; }
    public long TotalAmount { get; init; }
    public required string InvoicePayload { get; init; }
    public string? ShippingOptionId { get; init; }
}
```

### TelegramShippingAddress

```csharp
public sealed record TelegramShippingAddress(string CountryCode, string State, string City, string StreetLine1,
    string StreetLine2, string PostCode);
```

### TelegramShippingQuery

收货方式查询。用 `ITelegramPaymentApi.AnswerShippingQueryAsync` 回答。

```csharp
public sealed record TelegramShippingQuery : TelegramEventPayload
{
    public required string QueryId { get; init; }
    public required TelegramUser From { get; init; }
    public required string InvoicePayload { get; init; }
    public required TelegramShippingAddress ShippingAddress { get; init; }
}
```

### TelegramShippingOption

可选的收货方式。

```csharp
public sealed record TelegramShippingOption(string Id, string Title, IReadOnlyList<TelegramLabeledPrice> Prices);
```

### TelegramChatBoost

```csharp
public sealed record TelegramChatBoost : TelegramEventPayload
{
    public required TelegramChat Chat { get; init; }
    public required string BoostId { get; init; }

    /// <summary>premium、gift_code 或 giveaway。</summary>
    public required string Source { get; init; }

    /// <summary>提供助力的用户；部分抽奖来源没有。</summary>
    public TelegramUser? User { get; init; }

    public DateTimeOffset? ExpirationDate { get; init; }
    public bool IsRemoved { get; init; }
}
```

## 接口参数类型

### TelegramUploadFile

要随原始调用上传的文件，在参数中以 `attach://Name` 引用，或直接作为同名参数。

```csharp
public sealed record TelegramUploadFile(string Name, string FileName, Stream Content);
```
