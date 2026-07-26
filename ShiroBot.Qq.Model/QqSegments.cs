namespace ShiroBot.Qq.Model;

/// <summary>QQ 入站消息段基类(协议无关,由具体适配器从 Milky/OneBot 等映射)。</summary>
public abstract record QqIncomingSegment;

public sealed record QqTextIncoming(string Text) : QqIncomingSegment;

public sealed record QqMentionIncoming(long UserId, string Name) : QqIncomingSegment;

public sealed record QqMentionAllIncoming : QqIncomingSegment;

/// <summary>QQ 表情。</summary>
public sealed record QqFaceIncoming(string FaceId, bool IsLarge = false) : QqIncomingSegment;

public sealed record QqReplyIncoming(long MessageSeq) : QqIncomingSegment
{
    public long SenderId { get; init; }
    public string? SenderName { get; init; }
    public DateTimeOffset? Time { get; init; }
    public IReadOnlyList<QqIncomingSegment> Segments { get; init; } = [];
}

public sealed record QqImageIncoming(string ResourceId, string TempUrl) : QqIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Summary { get; init; }

    /// <summary>是否闪照/表情图等子类型(协议自定义字符串)。</summary>
    public string? SubType { get; init; }
}

public sealed record QqRecordIncoming(string ResourceId, string TempUrl) : QqIncomingSegment
{
    public TimeSpan Duration { get; init; }
}

public sealed record QqVideoIncoming(string ResourceId, string TempUrl) : QqIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed record QqFileIncoming(string FileId, string FileName, long FileSize) : QqIncomingSegment
{
    public string? FileHash { get; init; }
}

/// <summary>合并转发。</summary>
public sealed record QqForwardIncoming(string ForwardId) : QqIncomingSegment
{
    public string? Title { get; init; }
    public IReadOnlyList<string> Preview { get; init; } = [];
    public string? Summary { get; init; }
}

/// <summary>商城表情。</summary>
public sealed record QqMarketFaceIncoming(string EmojiId, string Url) : QqIncomingSegment
{
    public int EmojiPackageId { get; init; }
    public string? Key { get; init; }
    public string? Summary { get; init; }
}

/// <summary>小程序 / 卡片消息。</summary>
public sealed record QqLightAppIncoming(string AppName, string JsonPayload) : QqIncomingSegment;

public sealed record QqXmlIncoming(int ServiceId, string XmlPayload) : QqIncomingSegment;

public sealed record QqMarkdownIncoming(string Content) : QqIncomingSegment;

// ─── 出站 ───

/// <summary>QQ 出站消息段基类。</summary>
public abstract record QqOutgoingSegment;

public sealed record QqTextOutgoing(string Text) : QqOutgoingSegment;

public sealed record QqMentionOutgoing(long UserId) : QqOutgoingSegment;

public sealed record QqMentionAllOutgoing : QqOutgoingSegment;

public sealed record QqFaceOutgoing(string FaceId, bool IsLarge = false) : QqOutgoingSegment;

public sealed record QqReplyOutgoing(long MessageSeq) : QqOutgoingSegment;

/// <summary>Uri 支持 http(s)://、file://、base64://。</summary>
public sealed record QqImageOutgoing(string Uri) : QqOutgoingSegment
{
    public string? Summary { get; init; }

    /// <summary>协议自定义子类型(如 normal / sticker)。</summary>
    public string? SubType { get; init; }
}

public sealed record QqRecordOutgoing(string Uri) : QqOutgoingSegment;

public sealed record QqVideoOutgoing(string Uri) : QqOutgoingSegment
{
    public string? ThumbUri { get; init; }
}

/// <summary>小程序 / 卡片消息(出站)。</summary>
public sealed record QqLightAppOutgoing(string JsonPayload) : QqOutgoingSegment;

/// <summary>合并转发的一条消息。</summary>
public sealed record QqForwardedMessage(long UserId, string SenderName, IReadOnlyList<QqOutgoingSegment> Segments)
{
    /// <summary>消息展示时间,null 使用当前时间。</summary>
    public DateTimeOffset? Time { get; init; }
}

/// <summary>合并转发。</summary>
public sealed record QqForwardOutgoing(IReadOnlyList<QqForwardedMessage> Messages) : QqOutgoingSegment
{
    public string? Title { get; init; }
    public IReadOnlyList<string>? Preview { get; init; }
    public string? Summary { get; init; }
    public string? Prompt { get; init; }
}
