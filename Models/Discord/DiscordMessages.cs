using System.Text.Json;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Discord;

/// <summary>Embed（卡片）。发送限制：标题 256、描述 4096、字段 25 个、总文本 6000 字符。</summary>
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

public sealed record DiscordEmbedFooter(string Text)
{
    public string? IconUrl { get; init; }
}

public sealed record DiscordEmbedAuthor(string Name)
{
    public string? Url { get; init; }
    public string? IconUrl { get; init; }
}

/// <param name="Inline">与相邻的内联字段并排显示。</param>
public sealed record DiscordEmbedField(string Name, string Value, bool Inline = false);

/// <summary>按钮或下拉选项上显示的表情：Unicode 用 <see cref="Name"/>，自定义表情还需 <see cref="Id"/>。</summary>
public sealed record DiscordComponentEmoji(string Name)
{
    public ulong? Id { get; init; }
    public bool Animated { get; init; }
}

/// <summary>消息组件。消息最多 5 行 <see cref="DiscordActionRow"/>。</summary>
public abstract record DiscordComponent;

/// <summary>一行组件：最多 5 个按钮，或 1 个下拉菜单。</summary>
public sealed record DiscordActionRow(IReadOnlyList<DiscordComponent> Components) : DiscordComponent;

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

/// <summary>按钮。非链接按钮点击后产生 <see cref="DiscordInteractionKind.MessageComponent"/> 交互。</summary>
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

/// <summary>下拉菜单的文本选项。</summary>
public sealed record DiscordSelectOption(string Label, string Value)
{
    public string? Description { get; init; }
    public DiscordComponentEmoji? Emoji { get; init; }
    public bool Default { get; init; }
}

/// <summary>下拉菜单；选择结果在交互的 <see cref="DiscordInteraction.Values"/> 中。</summary>
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

public enum DiscordTextInputStyle
{
    Short = 1,
    Paragraph = 2
}

/// <summary>弹窗中的文本输入框；提交值在 <see cref="DiscordInteraction.ModalValues"/> 中。</summary>
public sealed record DiscordTextInput(string CustomId, string Label) : DiscordComponent
{
    public DiscordTextInputStyle Style { get; init; } = DiscordTextInputStyle.Short;
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public bool Required { get; init; } = true;
    public string? Value { get; init; }
    public string? Placeholder { get; init; }
}

/// <summary>未建模的组件（例如 Components V2 的 Container、Section），按 Discord API 的 JSON 原样发送。</summary>
public sealed record DiscordRawComponent(JsonElement Json) : DiscordComponent;

/// <summary>控制消息中的提及是否真正通知对方。默认只通知被回复的用户以外显式列出的提及。</summary>
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

/// <summary>随消息上传的附件。</summary>
/// <param name="Uri">HTTP(S) 地址或适配器支持的本地资源 URI（file、base64）。</param>
public sealed record DiscordAttachment(string Uri, string FileName)
{
    /// <summary>替代文本，0–1024 个字符。</summary>
    public string? Description { get; init; }

    /// <summary>以剧透遮罩显示（文件名加 SPOILER_ 前缀）。</summary>
    public bool IsSpoiler { get; init; }
}

/// <summary>投票。</summary>
public sealed record DiscordPollContent(string Question, IReadOnlyList<DiscordPollAnswerContent> Answers)
{
    /// <summary>1–768 小时，默认 24 小时。</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromHours(24);

    public bool AllowMultiselect { get; init; }
}

public sealed record DiscordPollAnswerContent(string Text)
{
    public DiscordComponentEmoji? Emoji { get; init; }
}

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

/// <summary>Discord 原生消息内容，用于发送、编辑和交互回复。</summary>
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

/// <summary>发送或编辑后的消息。</summary>
public sealed record DiscordSentMessage
{
    public required string MessageId { get; init; }

    /// <summary>所在 SDK 频道；私聊为 DM 频道 ID。</summary>
    public required Channel Channel { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>上传附件的 CDN 地址。</summary>
    public IReadOnlyList<string> AttachmentUrls { get; init; } = [];
}

/// <summary>
///     Discord 消息的原生资料，放在 <see cref="MessageEvent.Raw"/>。<see cref="Json"/> 保留原始 Message，
///     可按需读取本模型未列出的字段。
/// </summary>
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

/// <summary>
///     消息中没有通用 SDK 段的 Discord 内容，以 <see cref="RawSegment"/> 上报：<c>Platform</c> 为 discord，
///     <c>Kind</c> 为下列常量，<c>Payload</c> 为对应记录。发送时也接受 <c>Kind</c> 为 embed、<c>Payload</c> 为 <see cref="DiscordEmbed"/> 的段。
/// </summary>
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

public enum DiscordStickerFormat
{
    Png = 1,
    Apng = 2,
    Lottie = 3,
    Gif = 4
}

public sealed record DiscordSticker(ulong StickerId, string Name, DiscordStickerFormat Format);

public sealed record DiscordPollAnswer(int AnswerId, string Text, int Count, bool MeVoted);

/// <summary>消息中的投票及当前结果。</summary>
public sealed record DiscordPoll
{
    public required string Question { get; init; }
    public IReadOnlyList<DiscordPollAnswer> Answers { get; init; } = [];
    public DateTimeOffset? Expiry { get; init; }
    public bool AllowMultiselect { get; init; }

    /// <summary>结果已最终统计。</summary>
    public bool IsFinalized { get; init; }
}

/// <summary>入站消息的组件原始 JSON。</summary>
public sealed record DiscordComponents(JsonElement Json);
