using System.Text.Json;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Telegram;

/// <summary>
///     Telegram 特有事件。适配器以 <see cref="PlatformEvent"/> 上报：<c>Kind</c> 为下列常量，<c>Raw</c> 为对应负载。
///     其他没有建模的 Update 以 Update 字段名为 <c>Kind</c>，<c>Raw</c> 为 <see cref="JsonElement"/>。
///     消息、按钮回调、成员加入/离开和 Reaction 照常映射为 SDK 事件。
/// </summary>
public static class TelegramEventKinds
{
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

/// <summary>Telegram 事件负载的公共字段。</summary>
public abstract record TelegramEventPayload
{
    /// <summary>Bot API Update ID；MTProto 适配器没有对应序号时为 0。</summary>
    public long UpdateId { get; init; }

    public DateTimeOffset Time { get; init; }

    /// <summary>原始对象，结构由 <see cref="JsonFormat"/> 说明。</summary>
    public JsonElement Json { get; init; }

    public TelegramJsonFormat JsonFormat { get; init; }
}

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

/// <summary>入群申请。用 <see cref="ITelegramChatApi.ApproveJoinRequestAsync"/> 或 <see cref="ITelegramChatApi.DeclineJoinRequestAsync"/> 处理。</summary>
public sealed record TelegramChatJoinRequest : TelegramEventPayload
{
    public required TelegramChat Chat { get; init; }
    public required TelegramUser User { get; init; }

    /// <summary>可在 5 分钟内向申请人发私信的会话 ID。</summary>
    public long UserChatId { get; init; }

    public string? Bio { get; init; }
    public TelegramInviteLink? InviteLink { get; init; }
}

/// <summary>inline 查询。用 <see cref="ITelegramInlineApi.AnswerInlineQueryAsync"/> 回答。</summary>
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

public sealed record TelegramChosenInlineResult : TelegramEventPayload
{
    public required string ResultId { get; init; }
    public required TelegramUser From { get; init; }
    public required string Query { get; init; }

    /// <summary>结果带内联键盘时提供，可用于编辑该 inline 消息。</summary>
    public string? InlineMessageId { get; init; }
}

/// <summary>投票状态变化事件的负载。</summary>
public sealed record TelegramPollUpdated : TelegramEventPayload
{
    public required TelegramPoll Poll { get; init; }
}

public sealed record TelegramPollAnswer : TelegramEventPayload
{
    public required string PollId { get; init; }

    /// <summary>投票的用户；匿名身份投票时为 null，见 <see cref="VoterChat"/>。</summary>
    public TelegramUser? User { get; init; }

    public TelegramChat? VoterChat { get; init; }

    /// <summary>选择的选项，从 0 开始；撤回投票时为空。</summary>
    public IReadOnlyList<int> OptionIds { get; init; } = [];
}

/// <summary>付款前确认。用 <see cref="ITelegramPaymentApi.AnswerPreCheckoutQueryAsync"/> 回答。</summary>
public sealed record TelegramPreCheckoutQuery : TelegramEventPayload
{
    public required string QueryId { get; init; }
    public required TelegramUser From { get; init; }
    public required string Currency { get; init; }
    public long TotalAmount { get; init; }
    public required string InvoicePayload { get; init; }
    public string? ShippingOptionId { get; init; }
}

public sealed record TelegramShippingAddress(string CountryCode, string State, string City, string StreetLine1,
    string StreetLine2, string PostCode);

/// <summary>收货方式查询。用 <see cref="ITelegramPaymentApi.AnswerShippingQueryAsync"/> 回答。</summary>
public sealed record TelegramShippingQuery : TelegramEventPayload
{
    public required string QueryId { get; init; }
    public required TelegramUser From { get; init; }
    public required string InvoicePayload { get; init; }
    public required TelegramShippingAddress ShippingAddress { get; init; }
}

/// <summary>可选的收货方式。</summary>
public sealed record TelegramShippingOption(string Id, string Title, IReadOnlyList<TelegramLabeledPrice> Prices);

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
