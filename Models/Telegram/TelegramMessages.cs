using System.Text.Json;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Telegram;

/// <summary>正文的格式。</summary>
public enum TelegramParseMode
{
    /// <summary>纯文本，原样发送。</summary>
    None,

    /// <summary>Telegram MarkdownV2，特殊字符需要转义。</summary>
    MarkdownV2,

    Html
}

/// <summary>
///     Telegram 原生消息的公共发送选项。目标会话使用 SDK <see cref="Channel"/>，话题频道 ID 为 <c>chatId:threadId</c>。
/// </summary>
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

/// <summary>消息下方或输入框中的键盘。</summary>
public abstract record TelegramReplyMarkup;

/// <summary>消息下方的内联键盘，最多 100 个按钮。</summary>
public sealed record TelegramInlineKeyboard(IReadOnlyList<IReadOnlyList<TelegramInlineButton>> Rows) : TelegramReplyMarkup;

/// <summary>内联按钮，必须且只能设置一种动作。</summary>
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

/// <summary>替换用户输入框的自定义键盘。</summary>
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

/// <summary>自定义键盘按钮；没有请求项时按下即发送 <see cref="Text"/>。</summary>
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

/// <summary>移除自定义键盘。</summary>
public sealed record TelegramRemoveKeyboard : TelegramReplyMarkup
{
    public bool Selective { get; init; }
}

/// <summary>让客户端直接进入回复本消息的状态。</summary>
public sealed record TelegramForceReply : TelegramReplyMarkup
{
    public string? InputFieldPlaceholder { get; init; }
    public bool Selective { get; init; }
}

/// <summary><see cref="ITelegramMessageApi.SendAsync"/> 可发送的 Telegram 原生内容。普通文本和媒体也可使用 SDK 消息服务。</summary>
public abstract record TelegramOutgoingContent;

/// <summary>格式化文本。</summary>
public sealed record TelegramTextContent(string Text) : TelegramOutgoingContent
{
    public TelegramParseMode ParseMode { get; init; }

    /// <summary>不生成链接预览。</summary>
    public bool DisableLinkPreview { get; init; }
}

public enum TelegramPollType
{
    Regular,
    Quiz
}

/// <summary>投票或测验，2–12 个选项。</summary>
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

/// <summary>位置；设置 <see cref="LivePeriod"/> 时为实时位置。</summary>
public sealed record TelegramLocationContent(double Latitude, double Longitude) : TelegramOutgoingContent
{
    /// <summary>误差半径，0–1500 米。</summary>
    public double? HorizontalAccuracy { get; init; }

    /// <summary>实时位置的更新时长，60 秒至 86400 秒。</summary>
    public TimeSpan? LivePeriod { get; init; }

    /// <summary>移动方向，1–360 度，仅实时位置。</summary>
    public int? Heading { get; init; }
}

/// <summary>地点。</summary>
public sealed record TelegramVenueContent(double Latitude, double Longitude, string Title, string Address) : TelegramOutgoingContent
{
    public string? FoursquareId { get; init; }
    public string? GooglePlaceId { get; init; }
}

/// <summary>联系人名片。</summary>
public sealed record TelegramContactContent(string PhoneNumber, string FirstName) : TelegramOutgoingContent
{
    public string? LastName { get; init; }

    /// <summary>vCard 格式的附加信息，0–2048 字节。</summary>
    public string? VCard { get; init; }
}

/// <summary>随机动画表情，Telegram 决定点数。</summary>
/// <param name="Emoji">🎲、🎯、🏀、⚽、🎳 或 🎰。</param>
public sealed record TelegramDiceContent(string Emoji = "🎲") : TelegramOutgoingContent;

/// <summary>贴纸。</summary>
/// <param name="Sticker">file_id、HTTP(S) 地址，或适配器支持的本地资源 URI（file、base64）。</param>
public sealed record TelegramStickerContent(string Sticker) : TelegramOutgoingContent
{
    /// <summary>新上传贴纸对应的表情。</summary>
    public string? Emoji { get; init; }
}

public enum TelegramMediaKind
{
    Photo,
    Video,
    Audio,
    Document
}

/// <summary>相册中的一项。</summary>
/// <param name="Uri">file_id（<c>tgfile://</c> 前缀可选）、HTTP(S) 地址，或适配器支持的本地资源 URI。</param>
public sealed record TelegramMediaItem(TelegramMediaKind Kind, string Uri)
{
    public string? Caption { get; init; }
    public TelegramParseMode ParseMode { get; init; }
    public string? FileName { get; init; }

    /// <summary>以遮罩显示，点击后才可见；仅图片和视频。</summary>
    public bool HasSpoiler { get; init; }
}

/// <summary>
///     相册，2–10 项，作为一组消息发送。图片和视频可以混排；音频只能与音频、文件只能与文件组成相册。
///     相册不能附带键盘。
/// </summary>
public sealed record TelegramMediaGroupContent(IReadOnlyList<TelegramMediaItem> Items) : TelegramOutgoingContent;

/// <summary>价格项，金额以货币最小单位计；Telegram Stars（XTR）以 Star 计。</summary>
public sealed record TelegramLabeledPrice(string Label, long Amount);

/// <summary>发票。使用 Telegram Stars 时 <see cref="Currency"/> 为 XTR，且不需要支付服务商令牌。</summary>
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

/// <summary>发送后的消息。相册返回每一条。</summary>
public sealed record TelegramSentMessage
{
    public required string MessageId { get; init; }

    /// <summary>所在 SDK 频道。</summary>
    public required Channel Channel { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>相册的分组 ID。</summary>
    public string? MediaGroupId { get; init; }
}

/// <summary>正在进行的动作，显示在会话标题下，约 5 秒后或发送消息时消失。</summary>
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

/// <summary>回答按钮回调时的提示。</summary>
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

// ─── inline 模式 ───

/// <summary>inline 查询的一项结果。</summary>
/// <param name="Id">1–64 字节，在本次回答内唯一。</param>
public abstract record TelegramInlineResult(string Id)
{
    public TelegramInlineKeyboard? ReplyMarkup { get; init; }
}

/// <summary>选择后发送一段文本。</summary>
public sealed record TelegramInlineArticle(string Id, string Title, string MessageText) : TelegramInlineResult(Id)
{
    public TelegramParseMode ParseMode { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? ThumbnailUrl { get; init; }
}

/// <summary>选择后发送图片。</summary>
/// <param name="Photo">JPEG 的 HTTP(S) 地址或已有的 file_id。</param>
public sealed record TelegramInlinePhoto(string Id, string Photo) : TelegramInlineResult(Id)
{
    /// <summary>使用地址时必填。</summary>
    public string? ThumbnailUrl { get; init; }

    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Caption { get; init; }
    public TelegramParseMode ParseMode { get; init; }
}

/// <summary>选择后发送已有的贴纸。</summary>
public sealed record TelegramInlineSticker(string Id, string StickerFileId) : TelegramInlineResult(Id);

/// <summary>回答 inline 查询的选项。</summary>
public sealed record TelegramInlineAnswerOptions
{
    /// <summary>服务器缓存结果的时长，默认 300 秒。</summary>
    public TimeSpan? CacheTime { get; init; }

    /// <summary>只为发起查询的用户缓存。</summary>
    public bool IsPersonal { get; init; }

    /// <summary>用户滚动到底部时下一次查询携带的 offset；空字符串表示没有更多结果。</summary>
    public string? NextOffset { get; init; }
}

// ─── 入站内容 ───

/// <summary>
///     Telegram 消息的原生资料，放在 <see cref="MessageEvent.Raw"/>。<see cref="Json"/> 保留原始消息，
///     结构由 <see cref="JsonFormat"/> 说明，可按需读取本模型未列出的字段。
/// </summary>
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

public enum TelegramForwardOriginKind
{
    User,

    /// <summary>隐藏了账号的用户，只有 <see cref="TelegramForwardOrigin.SenderName"/>。</summary>
    HiddenUser,
    Chat,
    Channel
}

/// <summary>转发来源。</summary>
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

/// <summary>
///     消息中没有通用 SDK 段的 Telegram 内容，以 <see cref="RawSegment"/> 上报：
///     <c>Platform</c> 为 telegram，<c>Kind</c> 为下列常量，<c>Payload</c> 为对应记录。
///     其他未建模内容的 <c>Kind</c> 为 Bot API 字段名，<c>Payload</c> 为 <see cref="JsonElement"/>。
/// </summary>
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

public sealed record TelegramLocation(double Latitude, double Longitude)
{
    public double? HorizontalAccuracy { get; init; }
    public TimeSpan? LivePeriod { get; init; }
    public int? Heading { get; init; }
}

public sealed record TelegramVenue(TelegramLocation Location, string Title, string Address)
{
    public string? FoursquareId { get; init; }
    public string? GooglePlaceId { get; init; }
}

public sealed record TelegramContact(string PhoneNumber, string FirstName)
{
    public string? LastName { get; init; }

    /// <summary>联系人是 Telegram 用户时的 ID。</summary>
    public long? UserId { get; init; }

    public string? VCard { get; init; }
}

public sealed record TelegramDice(string Emoji, int Value);

public sealed record TelegramPollOption(string Text, int VoterCount);

/// <summary>投票的当前状态。</summary>
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

/// <summary>成功付款的服务消息。</summary>
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

/// <summary>Telegram 格式实体（粗体、剧透、代码、链接、自定义表情等）。偏移和长度以 UTF-16 代码单元计。</summary>
public sealed record TelegramTextEntity(string Type, int Offset, int Length)
{
    public string? Url { get; init; }
    public TelegramUser? User { get; init; }
    public string? Language { get; init; }
    public string? CustomEmojiId { get; init; }
}

/// <summary>消息文本或说明及其格式实体；SDK 文本段只保留纯文本。</summary>
public sealed record TelegramFormattedText(string Text, IReadOnlyList<TelegramTextEntity> Entities);
