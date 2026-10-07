namespace ShiroBot.SDK.Models;

/// <summary>
/// 平台无关的入站消息事件。
/// </summary>
public sealed record MessageEvent : BotEvent
{
    public required string MessageId { get; init; }

    public required Channel Channel { get; init; }

    /// <summary>发送者。群聊时携带成员信息。</summary>
    public required User Sender { get; init; }

    /// <summary>发送者在当前渠道内的成员信息，私聊时为 null。</summary>
    public Member? Member { get; init; }

    public required IReadOnlyList<MessageSegment> Segments { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>是否为私聊消息。</summary>
    public MessageReference Reference => new(InstanceId ?? throw new InvalidOperationException("Message has no source instance."), Channel, MessageId);

    public bool IsDirect => Channel.Type == ChannelType.Direct;

    /// <summary>消息纯文本内容（拼接所有文本段）。</summary>
    public string GetPlainText() =>
        string.Concat(Segments.OfType<TextSegment>().Select(segment => segment.Text));

    public bool HasMention() => Segments.OfType<MentionSegment>().Any();

    public bool HasMention(string userId) =>
        Segments.OfType<MentionSegment>().Any(segment => segment.UserId == userId);

    public bool HasMentionAll() => Segments.OfType<MentionAllSegment>().Any();

    public QuoteSegment? GetQuote() => Segments.OfType<QuoteSegment>().FirstOrDefault();
}

/// <summary>
/// 发送消息的结果。
/// </summary>
public sealed record SentMessage(string MessageId)
{
    public IReadOnlyList<MessageTransformation> Transformations { get; init; } = [];

    public MessageReference? Reference { get; init; }

    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>是否发送成功。宿主拦截适配器发送异常时为 false，且 MessageId 为空。</summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>失败说明。适配器的原始异常由宿主记入适配器日志。</summary>
    public string? ErrorMessage { get; init; }
}
