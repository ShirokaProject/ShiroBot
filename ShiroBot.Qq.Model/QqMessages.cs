namespace ShiroBot.Qq.Model;

/// <summary>QQ 入站消息(协议无关)。</summary>
public abstract record QqIncomingMessage
{
    public required long PeerId { get; init; }
    public required long MessageSeq { get; init; }
    public required long SenderId { get; init; }
    public DateTimeOffset Time { get; init; }
    public IReadOnlyList<QqIncomingSegment> Segments { get; init; } = [];

    public abstract QqMessageScene Scene { get; }

    public string GetPlainText() =>
        string.Concat(Segments.OfType<QqTextIncoming>().Select(segment => segment.Text));
}

/// <summary>好友消息。</summary>
public sealed record QqFriendMessage : QqIncomingMessage
{
    public required QqFriend Friend { get; init; }
    public override QqMessageScene Scene => QqMessageScene.Friend;
}

/// <summary>群消息。</summary>
public sealed record QqGroupMessage : QqIncomingMessage
{
    public required QqGroup Group { get; init; }
    public required QqGroupMember GroupMember { get; init; }
    public override QqMessageScene Scene => QqMessageScene.Group;
}

/// <summary>临时会话消息。</summary>
public sealed record QqTempMessage : QqIncomingMessage
{
    public QqGroup? Group { get; init; }
    public override QqMessageScene Scene => QqMessageScene.Temp;
}

/// <summary>合并转发内的一条消息。</summary>
public sealed record QqForwardedIncomingMessage
{
    public long MessageSeq { get; init; }
    public string? SenderName { get; init; }
    public string? AvatarUrl { get; init; }
    public DateTimeOffset Time { get; init; }
    public IReadOnlyList<QqIncomingSegment> Segments { get; init; } = [];
}
