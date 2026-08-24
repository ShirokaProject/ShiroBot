namespace ShiroBot.Model.QQ;

/// <summary>QQ 入站消息(协议无关)。</summary>
public abstract record QIncomingMessage
{
    public required long PeerId { get; init; }
    public required long MessageSeq { get; init; }
    public required long SenderId { get; init; }
    public DateTimeOffset Time { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];

    public abstract QMessageScene Scene { get; }

    public string GetPlainText() =>
        string.Concat(Segments.OfType<QIncomingText>().Select(segment => segment.Text));
}

/// <summary>好友消息。</summary>
public sealed record QFriendMessage : QIncomingMessage
{
    public required QFriend Friend { get; init; }
    public override QMessageScene Scene => QMessageScene.Friend;
}

/// <summary>群消息。</summary>
public sealed record QGroupMessage : QIncomingMessage
{
    public required QGroup Group { get; init; }
    public required QGroupMember GroupMember { get; init; }
    public override QMessageScene Scene => QMessageScene.Group;
}

/// <summary>临时会话消息。</summary>
public sealed record QTempMessage : QIncomingMessage
{
    public QGroup? Group { get; init; }
    public override QMessageScene Scene => QMessageScene.Temp;
}

/// <summary>合并转发内的一条消息。</summary>
public sealed record QForwardedIncomingMessage
{
    public long MessageSeq { get; init; }
    public string? SenderName { get; init; }
    public string? AvatarUrl { get; init; }
    public DateTimeOffset Time { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];
}

/// <summary>QQ 原生消息发送结果。</summary>
public sealed record QSentMessage(long MessageSeq, DateTimeOffset Time);
