# Discord C# 类型参考

命名空间：`ShiroBot.Model.Discord`。

`required` 属性必须初始化；`init` 属性在创建对象时设置；位置式 record 使用所列构造参数。继承类型同时拥有基类属性。

## 实体、权限与枚举

### DiscordUser

Discord 用户的协议无关表示。

```csharp
public sealed record DiscordUser
{
    public required ulong UserId { get; init; }
    public required string Username { get; init; }
    public string? GlobalName { get; init; }
    public string? Nickname { get; init; }
    public bool IsBot { get; init; }

    /// <summary>头像 CDN 地址；用户未设置头像时为 null。</summary>
    public string? AvatarUrl { get; init; }

    public string DisplayName => Nickname ?? GlobalName ?? Username;
}
```

### DiscordPermissions

权限位，与 Discord API 的 permissions 字符串一一对应。

```csharp
[Flags]
public enum DiscordPermissions : ulong
{
    None = 0,
    CreateInstantInvite = 1UL << 0,
    KickMembers = 1UL << 1,
    BanMembers = 1UL << 2,
    Administrator = 1UL << 3,
    ManageChannels = 1UL << 4,
    ManageGuild = 1UL << 5,
    AddReactions = 1UL << 6,
    ViewAuditLog = 1UL << 7,
    PrioritySpeaker = 1UL << 8,
    Stream = 1UL << 9,
    ViewChannel = 1UL << 10,
    SendMessages = 1UL << 11,
    SendTtsMessages = 1UL << 12,
    ManageMessages = 1UL << 13,
    EmbedLinks = 1UL << 14,
    AttachFiles = 1UL << 15,
    ReadMessageHistory = 1UL << 16,
    MentionEveryone = 1UL << 17,
    UseExternalEmojis = 1UL << 18,
    ViewGuildInsights = 1UL << 19,
    Connect = 1UL << 20,
    Speak = 1UL << 21,
    MuteMembers = 1UL << 22,
    DeafenMembers = 1UL << 23,
    MoveMembers = 1UL << 24,
    UseVoiceActivity = 1UL << 25,
    ChangeNickname = 1UL << 26,
    ManageNicknames = 1UL << 27,
    ManageRoles = 1UL << 28,
    ManageWebhooks = 1UL << 29,
    ManageGuildExpressions = 1UL << 30,
    UseApplicationCommands = 1UL << 31,
    RequestToSpeak = 1UL << 32,
    ManageEvents = 1UL << 33,
    ManageThreads = 1UL << 34,
    CreatePublicThreads = 1UL << 35,
    CreatePrivateThreads = 1UL << 36,
    UseExternalStickers = 1UL << 37,
    SendMessagesInThreads = 1UL << 38,
    UseEmbeddedActivities = 1UL << 39,
    ModerateMembers = 1UL << 40,
    ViewCreatorMonetizationAnalytics = 1UL << 41,
    UseSoundboard = 1UL << 42,
    CreateGuildExpressions = 1UL << 43,
    CreateEvents = 1UL << 44,
    UseExternalSounds = 1UL << 45,
    SendVoiceMessages = 1UL << 46,
    SetVoiceChannelStatus = 1UL << 48,
    SendPolls = 1UL << 49,
    UseExternalApps = 1UL << 50,
    PinMessages = 1UL << 51,
    BypassSlowmode = 1UL << 52
}
```

### DiscordGuild

服务器。

```csharp
public sealed record DiscordGuild
{
    public required ulong GuildId { get; init; }
    public required string Name { get; init; }
    public ulong OwnerId { get; init; }
    public string? IconUrl { get; init; }
    public string? Description { get; init; }

    /// <summary>近似成员数；只有查询时请求了计数才有值。</summary>
    public int? ApproximateMemberCount { get; init; }

    public int? ApproximatePresenceCount { get; init; }

    /// <summary>服务器特性，如 COMMUNITY、NEWS。</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    public string? PreferredLocale { get; init; }
}
```

### DiscordChannelType

频道类型，取值与 Discord API 相同。

```csharp
public enum DiscordChannelType
{
    GuildText = 0,
    DirectMessage = 1,
    GuildVoice = 2,
    GroupDirectMessage = 3,
    GuildCategory = 4,
    GuildAnnouncement = 5,
    AnnouncementThread = 10,
    PublicThread = 11,
    PrivateThread = 12,
    GuildStageVoice = 13,
    GuildDirectory = 14,
    GuildForum = 15,
    GuildMedia = 16
}
```

### DiscordChannel

频道、私聊或子区。SDK 频道 ID 即 `ChannelId`。

```csharp
public sealed record DiscordChannel
{
    public required ulong ChannelId { get; init; }
    public required DiscordChannelType Type { get; init; }
    public ulong? GuildId { get; init; }
    public string? Name { get; init; }
    public string? Topic { get; init; }

    /// <summary>所属分类；子区为父频道。</summary>
    public ulong? ParentId { get; init; }

    public int? Position { get; init; }
    public bool IsNsfw { get; init; }

    /// <summary>慢速模式间隔。</summary>
    public TimeSpan? RateLimitPerUser { get; init; }

    /// <summary>子区已归档。</summary>
    public bool IsArchived { get; init; }

    public bool IsLocked { get; init; }

    public bool IsThread => Type is DiscordChannelType.AnnouncementThread or DiscordChannelType.PublicThread
        or DiscordChannelType.PrivateThread;
}
```

### DiscordRole

角色。

```csharp
public sealed record DiscordRole
{
    public required ulong RoleId { get; init; }
    public required string Name { get; init; }

    /// <summary>RGB 颜色，0 表示无颜色。</summary>
    public int Color { get; init; }

    public DiscordPermissions Permissions { get; init; }
    public int Position { get; init; }

    /// <summary>在成员列表中单独分组显示。</summary>
    public bool IsHoisted { get; init; }

    public bool IsMentionable { get; init; }

    /// <summary>由集成或机器人管理，不能手动分配。</summary>
    public bool IsManaged { get; init; }
}
```

### DiscordMember

服务器成员。

```csharp
public sealed record DiscordMember
{
    public required DiscordUser User { get; init; }
    public required ulong GuildId { get; init; }
    public string? Nickname { get; init; }
    public IReadOnlyList<ulong> RoleIds { get; init; } = [];
    public DateTimeOffset? JoinedAt { get; init; }

    /// <summary>超时（禁言）结束时间。</summary>
    public DateTimeOffset? TimeoutUntil { get; init; }

    /// <summary>尚未通过成员筛选。</summary>
    public bool IsPending { get; init; }

    public bool IsOwner { get; init; }

    /// <summary>按角色计算的服务器级权限；群主和管理员为全部权限。</summary>
    public DiscordPermissions Permissions { get; init; }
}
```

### DiscordEmoji

对应主消息段：[`EmojiSegment`](../segments#emojisegment)

服务器自定义表情。

```csharp
public sealed record DiscordEmoji
{
    public required ulong EmojiId { get; init; }
    public required string Name { get; init; }
    public bool IsAnimated { get; init; }
    public bool IsAvailable { get; init; } = true;

    /// <summary>消息中引用该表情的文本，如 &lt;:name:id&gt;。</summary>
    public string Mention => (IsAnimated ? "<a:" : "<:") + Name + ":" + EmojiId + ">";
}
```

### DiscordInvite

邀请。

```csharp
public sealed record DiscordInvite
{
    public required string Code { get; init; }
    public string Url => "https://discord.gg/" + Code;
    public ulong? GuildId { get; init; }
    public ulong? ChannelId { get; init; }
    public DiscordUser? Inviter { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int? Uses { get; init; }
    public int? MaxUses { get; init; }

    /// <summary>临时成员，离线后自动移出。</summary>
    public bool IsTemporary { get; init; }
}
```

### DiscordInviteOptions

创建邀请的选项。

```csharp
public sealed record DiscordInviteOptions
{
    /// <summary>有效期，0 表示永久；默认 24 小时，最长 7 天。</summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>最多使用次数，0 表示不限。</summary>
    public int? MaxUses { get; init; }

    public bool Temporary { get; init; }

    /// <summary>不复用已有的相同设置邀请。</summary>
    public bool Unique { get; init; }
}
```

### DiscordBan

封禁记录。

```csharp
public sealed record DiscordBan(DiscordUser User, string? Reason);
```

### DiscordChannelOptions

创建或修改频道的设置；null 表示不设置或不修改。

```csharp
public sealed record DiscordChannelOptions
{
    public string? Name { get; init; }

    /// <summary>仅创建时生效。</summary>
    public DiscordChannelType? Type { get; init; }

    public string? Topic { get; init; }
    public ulong? ParentId { get; init; }
    public int? Position { get; init; }
    public bool? IsNsfw { get; init; }
    public TimeSpan? RateLimitPerUser { get; init; }
}
```

### DiscordRoleOptions

创建或修改角色的设置；null 表示不设置或不修改。

```csharp
public sealed record DiscordRoleOptions
{
    public string? Name { get; init; }
    public DiscordPermissions? Permissions { get; init; }
    public int? Color { get; init; }
    public bool? IsHoisted { get; init; }
    public bool? IsMentionable { get; init; }
}
```

## 消息内容、组件与入站内容

### DiscordEmbed

对应主消息段：[`CardSegment`](../segments#cardsegment)、[`RawSegment`](../segments#rawsegment)

Embed（卡片）。发送限制：标题 256、描述 4096、字段 25 个、总文本 6000 字符。

```csharp
public sealed record DiscordEmbed
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>左侧色条的 RGB 颜色。</summary>
    public int? Color { get; init; }

    public DiscordEmbedFooter? Footer { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public DiscordEmbedAuthor? Author { get; init; }
    public IReadOnlyList<DiscordEmbedField> Fields { get; init; } = [];
}
```

### DiscordEmbedFooter

```csharp
public sealed record DiscordEmbedFooter(string Text)
{
    public string? IconUrl { get; init; }
}
```

### DiscordEmbedAuthor

```csharp
public sealed record DiscordEmbedAuthor(string Name)
{
    public string? Url { get; init; }
    public string? IconUrl { get; init; }
}
```

### DiscordEmbedField

```csharp
public sealed record DiscordEmbedField(string Name, string Value, bool Inline = false);
```

### DiscordComponentEmoji

按钮或下拉选项上显示的表情：Unicode 用 `Name`，自定义表情还需 `Id`。

```csharp
public sealed record DiscordComponentEmoji(string Name)
{
    public ulong? Id { get; init; }
    public bool Animated { get; init; }
}
```

### DiscordComponent

消息组件。消息最多 5 行 `DiscordActionRow`。

```csharp
public abstract record DiscordComponent;
```

### DiscordActionRow

对应主消息段：[`MessageButton`](../segments#messagebutton)

一行组件：最多 5 个按钮，或 1 个下拉菜单。

```csharp
public sealed record DiscordActionRow(IReadOnlyList<DiscordComponent> Components) : DiscordComponent;
```

### DiscordButtonStyle

```csharp
public enum DiscordButtonStyle
{
    Primary = 1,
    Secondary = 2,
    Success = 3,
    Danger = 4,

    /// <summary>链接按钮，需要 <see cref="DiscordButton.Url"/>，点击不产生交互。</summary>
    Link = 5,

    /// <summary>购买 SKU，需要 <see cref="DiscordButton.SkuId"/>。</summary>
    Premium = 6
}
```

### DiscordButton

对应主消息段：[`MessageButton`](../segments#messagebutton)

按钮。非链接按钮点击后产生 `DiscordInteractionKind.MessageComponent` 交互。

```csharp
public sealed record DiscordButton : DiscordComponent
{
    public DiscordButtonStyle Style { get; init; } = DiscordButtonStyle.Primary;
    public string? Label { get; init; }
    public DiscordComponentEmoji? Emoji { get; init; }

    /// <summary>1–100 个字符，回传到交互的 <see cref="DiscordInteraction.CustomId"/>。</summary>
    public string? CustomId { get; init; }

    public string? Url { get; init; }
    public ulong? SkuId { get; init; }
    public bool Disabled { get; init; }
}
```

### DiscordSelectKind

```csharp
public enum DiscordSelectKind
{
    /// <summary>自定义文本选项。</summary>
    String = 3,
    User = 5,
    Role = 6,

    /// <summary>用户和角色。</summary>
    Mentionable = 7,
    Channel = 8
}
```

### DiscordSelectOption

下拉菜单的文本选项。

```csharp
public sealed record DiscordSelectOption(string Label, string Value)
{
    public string? Description { get; init; }
    public DiscordComponentEmoji? Emoji { get; init; }
    public bool Default { get; init; }
}
```

### DiscordSelectMenu

下拉菜单；选择结果在交互的 `DiscordInteraction.Values` 中。

```csharp
public sealed record DiscordSelectMenu(DiscordSelectKind Kind, string CustomId) : DiscordComponent
{
    /// <summary>文本菜单的选项，最多 25 个。</summary>
    public IReadOnlyList<DiscordSelectOption> Options { get; init; } = [];

    public string? Placeholder { get; init; }
    public int MinValues { get; init; } = 1;
    public int MaxValues { get; init; } = 1;

    /// <summary>频道菜单可选的频道类型。</summary>
    public IReadOnlyList<DiscordChannelType>? ChannelTypes { get; init; }

    public bool Disabled { get; init; }
}
```

### DiscordTextInputStyle

```csharp
public enum DiscordTextInputStyle
{
    Short = 1,
    Paragraph = 2
}
```

### DiscordTextInput

弹窗中的文本输入框；提交值在 `DiscordInteraction.ModalValues` 中。

```csharp
public sealed record DiscordTextInput(string CustomId, string Label) : DiscordComponent
{
    public DiscordTextInputStyle Style { get; init; } = DiscordTextInputStyle.Short;
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public bool Required { get; init; } = true;
    public string? Value { get; init; }
    public string? Placeholder { get; init; }
}
```

### DiscordRawComponent

未建模的组件（例如 Components V2 的 Container、Section），按 Discord API 的 JSON 原样发送。

```csharp
public sealed record DiscordRawComponent(JsonElement Json) : DiscordComponent;
```

### DiscordAllowedMentions

对应主消息段：[`MentionSegment`](../segments#mentionsegment)、[`MentionAllSegment`](../segments#mentionallsegment)

控制消息中的提及是否真正通知对方。默认只通知被回复的用户以外显式列出的提及。

```csharp
public sealed record DiscordAllowedMentions
{
    public bool Users { get; init; }
    public bool Roles { get; init; }

    /// <summary>@everyone 和 @here。</summary>
    public bool Everyone { get; init; }

    public IReadOnlyList<ulong> UserIds { get; init; } = [];
    public IReadOnlyList<ulong> RoleIds { get; init; } = [];

    /// <summary>回复时提及被回复的作者。</summary>
    public bool RepliedUser { get; init; } = true;

    public static DiscordAllowedMentions All { get; } = new() { Users = true, Roles = true, Everyone = true };
    public static DiscordAllowedMentions None { get; } = new() { RepliedUser = false };
}
```

### DiscordAttachment

对应主消息段：[`ImageSegment`](../segments#imagesegment)、[`AudioSegment`](../segments#audiosegment)、[`VideoSegment`](../segments#videosegment)、[`FileSegment`](../segments#filesegment)

随消息上传的附件。

```csharp
public sealed record DiscordAttachment(string Uri, string FileName)
{
    /// <summary>替代文本，0–1024 个字符。</summary>
    public string? Description { get; init; }

    /// <summary>以剧透遮罩显示（文件名加 SPOILER_ 前缀）。</summary>
    public bool IsSpoiler { get; init; }
}
```

### DiscordPollContent

投票。

```csharp
public sealed record DiscordPollContent(string Question, IReadOnlyList<DiscordPollAnswerContent> Answers)
{
    /// <summary>1–768 小时，默认 24 小时。</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromHours(24);

    public bool AllowMultiselect { get; init; }
}
```

### DiscordPollAnswerContent

```csharp
public sealed record DiscordPollAnswerContent(string Text)
{
    public DiscordComponentEmoji? Emoji { get; init; }
}
```

### DiscordMessageFlags

```csharp
[Flags]
public enum DiscordMessageFlags
{
    None = 0,

    /// <summary>不生成链接预览。</summary>
    SuppressEmbeds = 1 << 2,

    /// <summary>只有交互发起者可见；仅用于交互回复。</summary>
    Ephemeral = 1 << 6,

    /// <summary>静默消息，不推送通知。</summary>
    SuppressNotifications = 1 << 12,

    /// <summary>使用 Components V2，消息正文由组件组成，不能同时设置 Content 和 Embeds。</summary>
    IsComponentsV2 = 1 << 15
}
```

### DiscordMessageContent

对应主消息段：[`TextSegment`](../segments#textsegment)、[`MarkdownSegment`](../segments#markdownsegment)、[`QuoteSegment`](../segments#quotesegment)

Discord 原生消息内容，用于发送、编辑和交互回复。

```csharp
public sealed record DiscordMessageContent
{
    /// <summary>正文，最多 2000 个字符，支持 Discord Markdown。</summary>
    public string? Content { get; init; }

    public IReadOnlyList<DiscordEmbed> Embeds { get; init; } = [];
    public IReadOnlyList<DiscordComponent> Components { get; init; } = [];
    public IReadOnlyList<DiscordAttachment> Attachments { get; init; } = [];

    /// <summary>最多 3 个贴纸 ID。</summary>
    public IReadOnlyList<ulong> StickerIds { get; init; } = [];

    public DiscordPollContent? Poll { get; init; }
    public DiscordAllowedMentions? AllowedMentions { get; init; }
    public DiscordMessageFlags Flags { get; init; }
    public bool Tts { get; init; }

    /// <summary>回复的消息 ID；被回复消息不存在时仍然发送。</summary>
    public string? ReplyToMessageId { get; init; }
}
```

### DiscordSentMessage

发送或编辑后的消息。

```csharp
public sealed record DiscordSentMessage
{
    public required string MessageId { get; init; }

    /// <summary>所在 SDK 频道；私聊为 DM 频道 ID。</summary>
    public required Channel Channel { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>上传附件的 CDN 地址。</summary>
    public IReadOnlyList<string> AttachmentUrls { get; init; } = [];
}
```

### DiscordMessage

Discord 消息的原生资料，放在 `MessageEvent.Raw`。`Json` 保留原始 Message， 可按需读取本模型未列出的字段。

```csharp
public sealed record DiscordMessage
{
    public required ulong MessageId { get; init; }
    public required ulong ChannelId { get; init; }
    public ulong? GuildId { get; init; }
    public DiscordUser? Author { get; init; }

    /// <summary>服务器消息的作者成员资料。</summary>
    public DiscordMember? Member { get; init; }

    public DateTimeOffset Timestamp { get; init; }
    public DateTimeOffset? EditedTimestamp { get; init; }

    /// <summary>消息类型，0 为普通消息、19 为回复；其他值为系统消息。</summary>
    public int Type { get; init; }

    public DiscordMessageFlags Flags { get; init; }
    public bool IsPinned { get; init; }
    public bool IsTts { get; init; }

    /// <summary>通过 Webhook 发送时的 Webhook ID。</summary>
    public ulong? WebhookId { get; init; }

    /// <summary>斜杠命令等交互产生的消息。</summary>
    public ulong? InteractionUserId { get; init; }

    public IReadOnlyList<ulong> MentionedRoleIds { get; init; } = [];

    /// <summary>从此消息创建的子区。</summary>
    public ulong? ThreadId { get; init; }

    /// <summary>Discord API 原始 Message 对象。</summary>
    public JsonElement Json { get; init; }
}
```

### DiscordSegmentKinds

消息中没有通用 SDK 段的 Discord 内容，以 `RawSegment` 上报：`Platform` 为 discord， `Kind` 为下列常量，`Payload` 为对应记录。发送时也接受 `Kind` 为 embed、`Payload` 为 `DiscordEmbed` 的段。

```csharp
public static class DiscordSegmentKinds
{
    /// <summary><see cref="DiscordEmbed"/>。</summary>
    public const string Embed = "embed";

    /// <summary><see cref="DiscordSticker"/>。</summary>
    public const string Sticker = "sticker";

    /// <summary><see cref="DiscordPoll"/>。</summary>
    public const string Poll = "poll";

    /// <summary><see cref="DiscordComponents"/>：消息携带的组件。</summary>
    public const string Components = "components";
}
```

### DiscordStickerFormat

```csharp
public enum DiscordStickerFormat
{
    Png = 1,
    Apng = 2,
    Lottie = 3,
    Gif = 4
}
```

### DiscordSticker

对应主消息段：[`EmojiSegment`](../segments#emojisegment)、[`RawSegment`](../segments#rawsegment)

```csharp
public sealed record DiscordSticker(ulong StickerId, string Name, DiscordStickerFormat Format);
```

### DiscordPollAnswer

```csharp
public sealed record DiscordPollAnswer(int AnswerId, string Text, int Count, bool MeVoted);
```

### DiscordPoll

对应主消息段：[`RawSegment`](../segments#rawsegment)

消息中的投票及当前结果。

```csharp
public sealed record DiscordPoll
{
    public required string Question { get; init; }
    public IReadOnlyList<DiscordPollAnswer> Answers { get; init; } = [];
    public DateTimeOffset? Expiry { get; init; }
    public bool AllowMultiselect { get; init; }

    /// <summary>结果已最终统计。</summary>
    public bool IsFinalized { get; init; }
}
```

### DiscordComponents

对应主消息段：[`RawSegment`](../segments#rawsegment)

入站消息的组件原始 JSON。

```csharp
public sealed record DiscordComponents(JsonElement Json);
```

## 交互、应用命令与弹窗

### DiscordInteractionKind

```csharp
public enum DiscordInteractionKind
{
    Ping = 1,

    /// <summary>斜杠命令、用户命令或消息命令。</summary>
    ApplicationCommand = 2,

    /// <summary>按钮或下拉菜单。</summary>
    MessageComponent = 3,

    /// <summary>命令选项的自动补全。用 <see cref="IDiscordInteractionApi.AutocompleteAsync"/> 回答。</summary>
    ApplicationCommandAutocomplete = 4,

    /// <summary>提交弹窗。</summary>
    ModalSubmit = 5
}
```

### DiscordCommandType

```csharp
public enum DiscordCommandType
{
    /// <summary>斜杠命令。</summary>
    ChatInput = 1,

    /// <summary>右键用户菜单。</summary>
    User = 2,

    /// <summary>右键消息菜单。</summary>
    Message = 3,

    /// <summary>应用启动入口。</summary>
    PrimaryEntryPoint = 4
}
```

### DiscordCommandOptionType

```csharp
public enum DiscordCommandOptionType
{
    SubCommand = 1,
    SubCommandGroup = 2,
    String = 3,
    Integer = 4,
    Boolean = 5,
    User = 6,
    Channel = 7,
    Role = 8,
    Mentionable = 9,
    Number = 10,
    Attachment = 11
}
```

### DiscordCommandOptionValue

交互中收到的命令选项。子命令和子命令组的参数在 `Options` 中。

```csharp
public sealed record DiscordCommandOptionValue(string Name, DiscordCommandOptionType Type)
{
    /// <summary>原始取值：字符串、数字、布尔或 Snowflake 字符串。</summary>
    public JsonElement Value { get; init; }

    /// <summary>自动补全中当前正在输入的选项。</summary>
    public bool Focused { get; init; }

    public IReadOnlyList<DiscordCommandOptionValue> Options { get; init; } = [];

    public string? AsString() => Value.ValueKind == JsonValueKind.String ? Value.GetString() : Value.ValueKind == JsonValueKind.Undefined ? null : Value.ToString();
    public long? AsInteger() => Value.ValueKind == JsonValueKind.Number ? Value.GetInt64() : null;
    public double? AsNumber() => Value.ValueKind == JsonValueKind.Number ? Value.GetDouble() : null;
    public bool? AsBoolean() => Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? Value.GetBoolean() : null;
}
```

### DiscordInteraction

Discord 交互，放在 SDK `InteractionEvent.Raw`。按钮回调仍映射到 `ButtonId`/`Data`； 斜杠命令、下拉菜单、弹窗和自动补全的内容在此读取。

```csharp
public sealed record DiscordInteraction
{
    public required string InteractionId { get; init; }
    public required DiscordInteractionKind Kind { get; init; }
    public ulong ApplicationId { get; init; }
    public ulong? GuildId { get; init; }
    public ulong? ChannelId { get; init; }
    public DiscordUser? User { get; init; }

    /// <summary>服务器中的发起成员。</summary>
    public DiscordMember? Member { get; init; }

    /// <summary>命令名；子命令见 <see cref="Options"/>。</summary>
    public string? CommandName { get; init; }

    public ulong? CommandId { get; init; }
    public DiscordCommandType? CommandType { get; init; }
    public IReadOnlyList<DiscordCommandOptionValue> Options { get; init; } = [];

    /// <summary>用户命令或消息命令的目标 ID。</summary>
    public ulong? TargetId { get; init; }

    /// <summary>组件或弹窗的 custom_id。</summary>
    public string? CustomId { get; init; }

    /// <summary>下拉菜单的选择值（文本值或 ID）。</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>弹窗提交的输入：custom_id → 值。</summary>
    public IReadOnlyDictionary<string, string> ModalValues { get; init; } = new Dictionary<string, string>();

    /// <summary>组件所在消息的 ID。</summary>
    public string? MessageId { get; init; }

    /// <summary>用户客户端语言。</summary>
    public string? Locale { get; init; }

    public string? GuildLocale { get; init; }

    /// <summary>机器人在该频道的权限。</summary>
    public DiscordPermissions AppPermissions { get; init; }

    /// <summary>Discord API 原始 Interaction（不含交互令牌）。</summary>
    public JsonElement Json { get; init; }
}
```

### DiscordCommandChoice

命令选项的固定选项。

```csharp
public sealed record DiscordCommandChoice(string Name, object Value)
{
    /// <summary>本地化名称：语言代码 → 名称。</summary>
    public IReadOnlyDictionary<string, string>? NameLocalizations { get; init; }
}
```

### DiscordCommandOption

命令选项定义。必填选项必须排在可选选项之前。

```csharp
public sealed record DiscordCommandOption(DiscordCommandOptionType Type, string Name, string Description)
{
    public bool Required { get; init; }
    public IReadOnlyList<DiscordCommandChoice> Choices { get; init; } = [];

    /// <summary>子命令和子命令组的选项。</summary>
    public IReadOnlyList<DiscordCommandOption> Options { get; init; } = [];

    public IReadOnlyList<DiscordChannelType>? ChannelTypes { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }

    /// <summary>启用自动补全；不能与 <see cref="Choices"/> 同时使用。</summary>
    public bool Autocomplete { get; init; }

    public IReadOnlyDictionary<string, string>? NameLocalizations { get; init; }
    public IReadOnlyDictionary<string, string>? DescriptionLocalizations { get; init; }
}
```

### DiscordCommandContexts

```csharp
[Flags]
public enum DiscordCommandContexts
{
    None = 0,
    Guild = 1 << 0,

    /// <summary>与机器人的私聊。</summary>
    BotDirectMessage = 1 << 1,

    /// <summary>用户安装的应用在其他私聊和群聊中使用。</summary>
    PrivateChannel = 1 << 2
}
```

### DiscordApplicationCommand

应用命令定义。名称：斜杠命令为 1–32 个小写字符，用户/消息命令可含空格和大写。

```csharp
public sealed record DiscordApplicationCommand(string Name)
{
    public DiscordCommandType Type { get; init; } = DiscordCommandType.ChatInput;

    /// <summary>斜杠命令必填，1–100 个字符；用户和消息命令留空。</summary>
    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<DiscordCommandOption> Options { get; init; } = [];

    /// <summary>默认需要的成员权限；null 表示所有人可用，<see cref="DiscordPermissions.None"/> 表示仅管理员。</summary>
    public DiscordPermissions? DefaultMemberPermissions { get; init; }

    /// <summary>可使用的场景；null 使用 Discord 默认值。</summary>
    public DiscordCommandContexts? Contexts { get; init; }

    public bool Nsfw { get; init; }
    public IReadOnlyDictionary<string, string>? NameLocalizations { get; init; }
    public IReadOnlyDictionary<string, string>? DescriptionLocalizations { get; init; }
}
```

### DiscordRegisteredCommand

已注册的命令。

```csharp
public sealed record DiscordRegisteredCommand
{
    public required ulong CommandId { get; init; }
    public required string Name { get; init; }
    public DiscordCommandType Type { get; init; }
    public string Description { get; init; } = string.Empty;

    /// <summary>服务器命令所属服务器；全局命令为 null。</summary>
    public ulong? GuildId { get; init; }

    public ulong Version { get; init; }
}
```

### DiscordModal

弹窗，最多 5 个输入框。

```csharp
public sealed record DiscordModal(string CustomId, string Title, IReadOnlyList<DiscordTextInput> Inputs);
```

## 事件与事件 Kind

### DiscordEventKinds

Discord 特有事件。适配器以 `PlatformEvent` 上报：`Kind` 为 Gateway 事件名的小写形式， 下列常量的 `Raw` 为对应负载，其他事件的 `Raw` 为 `JsonElement`。 消息、删除、成员加入/离开、交互和 Reaction 照常映射为 SDK 事件。

```csharp
public static class DiscordEventKinds
{
    /// <summary>服务器可用或机器人加入服务器；<see cref="DiscordGuildAvailable"/>。</summary>
    public const string GuildCreate = "guild_create";

    /// <summary>服务器不可用或机器人被移出；<see cref="DiscordGuildUnavailable"/>。</summary>
    public const string GuildDelete = "guild_delete";

    public const string GuildUpdate = "guild_update";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ChannelCreate = "channel_create";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ChannelUpdate = "channel_update";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ChannelDelete = "channel_delete";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ThreadCreate = "thread_create";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ThreadUpdate = "thread_update";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ThreadDelete = "thread_delete";

    /// <summary>成员昵称、角色或超时变化；<see cref="DiscordMemberUpdated"/>。</summary>
    public const string GuildMemberUpdate = "guild_member_update";

    /// <summary><see cref="DiscordRoleChanged"/>。</summary>
    public const string GuildRoleCreate = "guild_role_create";

    /// <summary><see cref="DiscordRoleChanged"/>。</summary>
    public const string GuildRoleUpdate = "guild_role_update";

    /// <summary><see cref="DiscordRoleChanged"/>，<c>Role</c> 只有 ID。</summary>
    public const string GuildRoleDelete = "guild_role_delete";

    /// <summary><see cref="DiscordBanChanged"/>。</summary>
    public const string GuildBanAdd = "guild_ban_add";

    /// <summary><see cref="DiscordBanChanged"/>。</summary>
    public const string GuildBanRemove = "guild_ban_remove";

    /// <summary>加入、离开或切换语音频道，静音状态变化；<see cref="DiscordVoiceStateUpdated"/>。</summary>
    public const string VoiceStateUpdate = "voice_state_update";

    /// <summary>需要 Typing Intent；<see cref="DiscordTypingStarted"/>。</summary>
    public const string TypingStart = "typing_start";

    /// <summary><see cref="DiscordReactionsCleared"/>。</summary>
    public const string MessageReactionRemoveAll = "message_reaction_remove_all";

    /// <summary><see cref="DiscordReactionsCleared"/>，带 <c>Emoji</c>。</summary>
    public const string MessageReactionRemoveEmoji = "message_reaction_remove_emoji";

    /// <summary><see cref="DiscordPollVote"/>。</summary>
    public const string MessagePollVoteAdd = "message_poll_vote_add";

    /// <summary><see cref="DiscordPollVote"/>。</summary>
    public const string MessagePollVoteRemove = "message_poll_vote_remove";

    /// <summary><see cref="DiscordInviteChanged"/>。</summary>
    public const string InviteCreate = "invite_create";

    /// <summary><see cref="DiscordInviteChanged"/>。</summary>
    public const string InviteDelete = "invite_delete";
}
```

### DiscordEventPayload

Discord 事件负载的公共字段。

```csharp
public abstract record DiscordEventPayload
{
    public ulong? GuildId { get; init; }

    /// <summary>Gateway 原始 data。</summary>
    public JsonElement Json { get; init; }
}
```

### DiscordGuildAvailable

```csharp
public sealed record DiscordGuildAvailable : DiscordEventPayload
{
    public required DiscordGuild Guild { get; init; }

    /// <summary>机器人加入的时间。</summary>
    public DateTimeOffset? JoinedAt { get; init; }

    public int MemberCount { get; init; }
    public IReadOnlyList<DiscordChannel> Channels { get; init; } = [];
    public IReadOnlyList<DiscordRole> Roles { get; init; } = [];
}
```

### DiscordGuildUnavailable

```csharp
public sealed record DiscordGuildUnavailable : DiscordEventPayload
{
    /// <summary>true 为服务器暂时故障；false 为机器人被移出或服务器已删除。</summary>
    public bool IsOutage { get; init; }
}
```

### DiscordChannelChanged

```csharp
public sealed record DiscordChannelChanged : DiscordEventPayload
{
    public required DiscordChannel Channel { get; init; }
}
```

### DiscordMemberUpdated

```csharp
public sealed record DiscordMemberUpdated : DiscordEventPayload
{
    public required DiscordMember Member { get; init; }
}
```

### DiscordRoleChanged

```csharp
public sealed record DiscordRoleChanged : DiscordEventPayload
{
    public required ulong RoleId { get; init; }

    /// <summary>删除事件中为 null。</summary>
    public DiscordRole? Role { get; init; }
}
```

### DiscordBanChanged

```csharp
public sealed record DiscordBanChanged : DiscordEventPayload
{
    public required DiscordUser User { get; init; }
}
```

### DiscordVoiceStateUpdated

```csharp
public sealed record DiscordVoiceStateUpdated : DiscordEventPayload
{
    public required ulong UserId { get; init; }

    /// <summary>离开语音时为 null。</summary>
    public ulong? ChannelId { get; init; }

    public DiscordMember? Member { get; init; }
    public bool IsMuted { get; init; }
    public bool IsDeafened { get; init; }
    public bool IsSelfMuted { get; init; }
    public bool IsSelfDeafened { get; init; }
    public bool IsStreaming { get; init; }
    public bool IsVideoOn { get; init; }
}
```

### DiscordTypingStarted

```csharp
public sealed record DiscordTypingStarted : DiscordEventPayload
{
    public required ulong ChannelId { get; init; }
    public required ulong UserId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

### DiscordReactionsCleared

```csharp
public sealed record DiscordReactionsCleared : DiscordEventPayload
{
    public required ulong ChannelId { get; init; }
    public required ulong MessageId { get; init; }

    /// <summary>只清除某个表情时提供；Unicode 表情为字符，自定义表情为 name:id。</summary>
    public string? Emoji { get; init; }
}
```

### DiscordPollVote

```csharp
public sealed record DiscordPollVote : DiscordEventPayload
{
    public required ulong UserId { get; init; }
    public required ulong ChannelId { get; init; }
    public required ulong MessageId { get; init; }
    public int AnswerId { get; init; }
    public bool IsAdded { get; init; }
}
```

### DiscordInviteChanged

```csharp
public sealed record DiscordInviteChanged : DiscordEventPayload
{
    public required string Code { get; init; }
    public ulong ChannelId { get; init; }

    /// <summary>删除事件中为 null。</summary>
    public DiscordInvite? Invite { get; init; }
}
```

## 账号与接口参数类型

### DiscordAccountKind

适配器登录的账号类型。

```csharp
public enum DiscordAccountKind
{
    /// <summary>Developer Portal 创建的机器人（Bot Token）。</summary>
    Bot,

    /// <summary>
    ///     用户自己的账号。Discord 服务条款禁止自动化用户账号（self-bot），账号可能被封禁；是否使用由部署者自行承担。
    /// </summary>
    User
}
```

### DiscordStatus

```csharp
public enum DiscordStatus
{
    Online,
    Idle,

    /// <summary>请勿打扰。</summary>
    DoNotDisturb,
    Invisible
}
```

### DiscordActivityType

```csharp
public enum DiscordActivityType
{
    Playing = 0,

    /// <summary>需要 Twitch 或 YouTube 的 <see cref="DiscordActivity.Url"/>。</summary>
    Streaming = 1,
    Listening = 2,
    Watching = 3,

    /// <summary>自定义状态，文本在 <see cref="DiscordActivity.State"/>。</summary>
    Custom = 4,
    Competing = 5
}
```

### DiscordActivity

```csharp
public sealed record DiscordActivity(DiscordActivityType Type, string Name)
{
    public string? State { get; init; }
    public string? Url { get; init; }
}
```

### DiscordPresence

账号的在线状态与活动。

```csharp
public sealed record DiscordPresence
{
    public DiscordStatus Status { get; init; } = DiscordStatus.Online;
    public DiscordActivity? Activity { get; init; }
    public bool Afk { get; init; }
}
```

### DiscordUploadFile

随原始请求上传的文件，按顺序作为 `files[n]` 发送。

```csharp
public sealed record DiscordUploadFile(string FileName, Stream Content);
```
