namespace ShiroBot.SDK.Models;

public enum InteractionAcknowledgement { Pending, Acknowledged, Failed, Unknown }
/// <summary>通用按钮回调；Kind/Raw 继续保留平台负载，不重复发布第二条事件。</summary>
public sealed record InteractionEvent : PlatformEvent
{
    public required string InteractionId { get; init; }
    public InteractionReference Reference => new(InstanceId ?? throw new InvalidOperationException("Interaction has no source instance."),
        Channel ?? throw new InvalidOperationException("Interaction has no channel."), InteractionId);
    public required User User { get; init; }
    public string? ButtonId { get; init; }
    public string? Data { get; init; }
    public string? MessageId { get; init; }
    public InteractionAcknowledgement Acknowledgement { get; init; }
    public bool IsAutomaticallyAcknowledged { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
public sealed record InteractionReference(string InstanceId, Channel Channel, string InteractionId);

public abstract record ReactionEmoji;
public sealed record UnicodeReactionEmoji(string Value) : ReactionEmoji;
/// <summary>平台表情只可用于同一实例。GuildId 可进一步限定自定义表情范围。</summary>
public sealed record PlatformReactionEmoji(string Id, string InstanceId) : ReactionEmoji
{
    public string? GuildId { get; init; }
}
[Flags]
public enum ReactionCapabilities { None = 0, Unicode = 1, PlatformEmoji = 2 }
public sealed record MessageReactionEvent : PlatformEvent
{
    public required string MessageId { get; init; }
    public required ReactionEmoji Emoji { get; init; }
    public bool IsAdded { get; init; }
    public User? User { get; init; }
    public int? Count { get; init; }
    public MessageReference Reference => new(InstanceId ?? throw new InvalidOperationException("Reaction has no source instance."),
        Channel ?? throw new InvalidOperationException("Reaction has no channel."), MessageId);
}
