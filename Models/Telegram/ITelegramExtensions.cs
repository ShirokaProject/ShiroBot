using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Telegram;

// 插件通过 Context.GetAdapterExtension<T>() 探测这些接口。会话参数接受数字 ID、@username 或 SDK 频道 ID
// （chatId:threadId 只在话题相关方法中使用话题部分）；用户和消息 ID 与 SDK 的 User.Id、MessageId 相同。
// 未实现的方法默认抛 NotSupportedException；平台权限仍由 Telegram 校验。

/// <summary>Telegram 会话、成员、话题和邀请管理。</summary>
public interface ITelegramChatApi
{
    Task<TelegramChat> GetChatAsync(string chatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<int> GetMemberCountAsync(string chatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<TelegramChatMember> GetMemberAsync(string chatId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>管理员列表（不含其他机器人）。Telegram 不提供完整成员列表。</summary>
    Task<IReadOnlyList<TelegramChatMember>> GetAdministratorsAsync(string chatId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>封禁成员；<paramref name="until"/> 为 null 或超过 366 天时为永久封禁。</summary>
    Task BanMemberAsync(string chatId, string userId, DateTimeOffset? until = null, bool revokeMessages = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>解除封禁。<paramref name="onlyIfBanned"/> 为 false 时，对仍在群内的成员调用会把他移出。</summary>
    Task UnbanMemberAsync(string chatId, string userId, bool onlyIfBanned = true,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>限制成员；<paramref name="until"/> 为 null 时永久有效。</summary>
    Task RestrictMemberAsync(string chatId, string userId, TelegramChatPermissions permissions,
        DateTimeOffset? until = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>设置管理员权限；所有权限为 false 时撤销管理员。</summary>
    Task PromoteMemberAsync(string chatId, string userId, TelegramAdminRights rights,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>设置机器人提升的管理员的头衔，0–16 个字符。</summary>
    Task SetAdministratorTitleAsync(string chatId, string userId, string title,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>设置所有成员的默认权限。</summary>
    Task SetPermissionsAsync(string chatId, TelegramChatPermissions permissions,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task SetTitleAsync(string chatId, string title, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task SetDescriptionAsync(string chatId, string? description, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>设置会话头像；<paramref name="imageUri"/> 为 HTTP(S) 地址或适配器支持的本地资源 URI。</summary>
    Task SetPhotoAsync(string chatId, string imageUri, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task DeletePhotoAsync(string chatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task PinMessageAsync(string chatId, string messageId, bool disableNotification = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>取消置顶；<paramref name="messageId"/> 为 null 时取消最近一条置顶。</summary>
    Task UnpinMessageAsync(string chatId, string? messageId = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>取消全部置顶；对话题频道 ID 只取消该话题的置顶。</summary>
    Task UnpinAllMessagesAsync(string chatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>重新生成主邀请链接，旧链接失效。</summary>
    Task<string> ExportInviteLinkAsync(string chatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<TelegramInviteLink> CreateInviteLinkAsync(string chatId, TelegramInviteLinkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<TelegramInviteLink> EditInviteLinkAsync(string chatId, string link, TelegramInviteLinkOptions options,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<TelegramInviteLink> RevokeInviteLinkAsync(string chatId, string link,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task ApproveJoinRequestAsync(string chatId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task DeclineJoinRequestAsync(string chatId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task LeaveChatAsync(string chatId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>创建话题；返回的 <see cref="TelegramForumTopic.ThreadId"/> 组成 SDK 频道 ID <c>chatId:threadId</c>。</summary>
    Task<TelegramForumTopic> CreateTopicAsync(string chatId, string name, int? iconColor = null,
        string? iconCustomEmojiId = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>修改话题；null 表示保持不变。</summary>
    Task EditTopicAsync(string chatId, long threadId, string? name = null, string? iconCustomEmojiId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task CloseTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task ReopenTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>删除话题及其中的全部消息。</summary>
    Task DeleteTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Telegram 原生消息：投票、位置、相册、发票、编辑、转发、输入状态和回调提示。</summary>
public interface ITelegramMessageApi
{
    /// <summary>发送 Telegram 原生内容；相册返回每条消息，其他内容返回一条。</summary>
    Task<IReadOnlyList<TelegramSentMessage>> SendAsync(Channel channel, TelegramOutgoingContent content,
        TelegramSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>修改文本消息；<paramref name="replyMarkup"/> 为 null 时移除内联键盘。</summary>
    Task EditTextAsync(Channel channel, string messageId, string text,
        TelegramParseMode parseMode = TelegramParseMode.None, TelegramInlineKeyboard? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>修改媒体消息的说明。</summary>
    Task EditCaptionAsync(Channel channel, string messageId, string? caption,
        TelegramParseMode parseMode = TelegramParseMode.None, TelegramInlineKeyboard? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>替换或移除（null）内联键盘。</summary>
    Task EditReplyMarkupAsync(Channel channel, string messageId, TelegramInlineKeyboard? replyMarkup,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>修改 inline 模式发送的消息文本，ID 来自 <see cref="TelegramChosenInlineResult.InlineMessageId"/>。</summary>
    Task EditInlineTextAsync(string inlineMessageId, string text,
        TelegramParseMode parseMode = TelegramParseMode.None, TelegramInlineKeyboard? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>转发并显示来源。</summary>
    Task<TelegramSentMessage> ForwardAsync(Channel target, Channel source, string messageId,
        bool disableNotification = false, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>复制内容，不显示来源；投票、发票等无法复制。返回新消息 ID。</summary>
    Task<string> CopyAsync(Channel target, Channel source, string messageId, TelegramSendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>批量删除，1–100 条；48 小时前的消息通常无法删除。</summary>
    Task DeleteMessagesAsync(Channel channel, IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>显示“正在输入”等状态约 5 秒。</summary>
    Task SendChatActionAsync(Channel channel, TelegramChatAction action, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>结束投票并返回最终结果。</summary>
    Task<TelegramPoll> StopPollAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    ///     带提示回答按钮回调，<paramref name="interactionId"/> 为 <see cref="InteractionEvent.InteractionId"/>。
    ///     适配器自动确认回调时，此前的空确认已经发出，提示不会再显示；需要提示时关闭适配器的自动确认。
    /// </summary>
    Task AnswerCallbackAsync(string interactionId, TelegramCallbackAnswer answer,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>适配器是否会自动确认按钮回调。为 true 时 <see cref="AnswerCallbackAsync"/> 的提示不生效。</summary>
    bool AutoAnswersCallbacks => true;
}

/// <summary>inline 模式（在任意会话输入 @机器人 查询）。需要在 BotFather 开启。</summary>
public interface ITelegramInlineApi
{
    /// <summary>回答 <see cref="TelegramEventKinds.InlineQuery"/>，最多 50 项结果；需在 10 秒内调用。</summary>
    Task AnswerInlineQueryAsync(string queryId, IReadOnlyList<TelegramInlineResult> results,
        TelegramInlineAnswerOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>发票与 Telegram Stars 支付。发送发票使用 <see cref="ITelegramMessageApi.SendAsync"/> 和 <see cref="TelegramInvoiceContent"/>。</summary>
public interface ITelegramPaymentApi
{
    /// <summary>确认或拒绝付款；<paramref name="errorMessage"/> 在拒绝时显示给用户。</summary>
    Task AnswerPreCheckoutQueryAsync(string queryId, bool ok, string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>返回可选的收货方式，或拒绝该地址。</summary>
    Task AnswerShippingQueryAsync(string queryId, bool ok, IReadOnlyList<TelegramShippingOption>? options = null,
        string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>退还 Telegram Stars 付款。</summary>
    Task RefundStarPaymentAsync(string userId, string telegramPaymentChargeId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>机器人当前的 Telegram Stars 余额。</summary>
    Task<long> GetStarBalanceAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>机器人资料与命令菜单。</summary>
public interface ITelegramBotProfileApi
{
    /// <summary>当前机器人的完整资料。</summary>
    Task<TelegramUser> GetMeAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>已在当前实例中见过的用户；Bot API 不提供按 ID 查询任意用户。</summary>
    TelegramUser? FindKnownUser(string userId)
    {
        return null;
    }

    /// <param name="languageCode">两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。</param>
    Task SetCommandsAsync(IReadOnlyList<TelegramBotCommand> commands, TelegramCommandScope? scope = null,
        string? languageCode = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<IReadOnlyList<TelegramBotCommand>> GetCommandsAsync(TelegramCommandScope? scope = null,
        string? languageCode = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task DeleteCommandsAsync(TelegramCommandScope? scope = null, string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task SetBotNameAsync(string? name, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>空会话中显示的介绍，0–512 个字符。</summary>
    Task SetBotDescriptionAsync(string? description, string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>资料页和分享链接中显示的简介，0–120 个字符。</summary>
    Task SetBotShortDescriptionAsync(string? description, string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>要随原始调用上传的文件，在参数中以 <c>attach://Name</c> 引用，或直接作为同名参数。</summary>
/// <param name="Content">请求发送时读取一次，调用方负责释放。</param>
public sealed record TelegramUploadFile(string Name, string FileName, Stream Content);

/// <summary>
///     直接调用任意 Bot API 方法，覆盖本模型没有建模的功能。只使用 System.Text.Json 类型，插件无需引用适配器程序集。
///     适配器负责长轮询：调用 getUpdates 或 setWebhook 会与之冲突。
/// </summary>
public interface ITelegramRawApi
{
    /// <summary>Bot API 版本，如 10.4。</summary>
    string BotApiVersion => string.Empty;

    /// <summary>调用方法并返回原始 result；失败抛出 <see cref="InvalidOperationException"/>，消息包含 Telegram 错误码和说明。</summary>
    /// <param name="method">方法名，如 <c>sendMessage</c>。</param>
    /// <param name="parameters">snake_case 参数。</param>
    /// <param name="files">随请求上传的文件；提供时使用 multipart。</param>
    Task<JsonElement> CallAsync(string method, JsonObject? parameters = null,
        IReadOnlyList<TelegramUploadFile>? files = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>每个 Update 的原始 JSON，先于 SDK 事件触发。</summary>
    event Func<JsonElement, Task>? UpdateReceived
    {
        add => throw new NotSupportedException();
        remove { }
    }
}
