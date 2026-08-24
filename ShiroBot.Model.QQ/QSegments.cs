namespace ShiroBot.Model.QQ;

/// <summary>QQ 入站消息段基类(协议无关,由具体适配器从 Milky/OneBot 等映射)。</summary>
public abstract record QIncomingSegment;

public sealed record QIncomingText(string Text) : QIncomingSegment;

public sealed record QIncomingMention(long UserId, string Name) : QIncomingSegment;

public sealed record QIncomingMentionAll : QIncomingSegment;

/// <summary>QQ 表情。</summary>
public sealed record QIncomingFace(string FaceId, bool IsLarge = false) : QIncomingSegment;

public sealed record QIncomingReply(long MessageSeq) : QIncomingSegment
{
    public long SenderId { get; init; }
    public string? SenderName { get; init; }
    public DateTimeOffset? Time { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];
}

public sealed record QIncomingImage(string ResourceId, string TempUrl) : QIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Summary { get; init; }

    /// <summary>是否闪照/表情图等子类型(协议自定义字符串)。</summary>
    public string? SubType { get; init; }
}

public sealed record QIncomingRecord(string ResourceId, string TempUrl) : QIncomingSegment
{
    public TimeSpan Duration { get; init; }
}

public sealed record QIncomingVideo(string ResourceId, string TempUrl) : QIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed record QIncomingFile(string FileId, string FileName, long FileSize) : QIncomingSegment
{
    public string? FileHash { get; init; }
}

/// <summary>合并转发。</summary>
public sealed record QIncomingForward(string ForwardId) : QIncomingSegment
{
    public string? Title { get; init; }
    public IReadOnlyList<string> Preview { get; init; } = [];
    public string? Summary { get; init; }
}

/// <summary>商城表情。</summary>
public sealed record QIncomingMarketFace(string EmojiId, string Url) : QIncomingSegment
{
    public int EmojiPackageId { get; init; }
    public string? Key { get; init; }
    public string? Summary { get; init; }
}

/// <summary>小程序 / 卡片消息。</summary>
public sealed record QIncomingLightApp(string AppName, string JsonPayload) : QIncomingSegment;

public sealed record QIncomingXml(int ServiceId, string XmlPayload) : QIncomingSegment;

public sealed record QIncomingMarkdown(string Content) : QIncomingSegment;

// ─── 出站 ───

/// <summary>QQ 出站消息段基类。</summary>
public abstract record QOutgoingSegment;

public sealed record QOutgoingText(string Text) : QOutgoingSegment;

public sealed record QOutgoingMention(long UserId) : QOutgoingSegment;

public sealed record QOutgoingMentionAll : QOutgoingSegment;

public sealed record QOutgoingFace(string FaceId, bool IsLarge = false) : QOutgoingSegment;

public sealed record QOutgoingReply(long MessageSeq) : QOutgoingSegment;

/// <summary>Uri 支持 http(s)://、file://、base64://。</summary>
public sealed record QOutgoingImage(string Uri) : QOutgoingSegment
{
    public string? Summary { get; init; }

    /// <summary>协议自定义子类型(如 normal / sticker)。</summary>
    public string? SubType { get; init; }
}

public sealed record QOutgoingRecord(string Uri) : QOutgoingSegment;

public sealed record QOutgoingVideo(string Uri) : QOutgoingSegment
{
    public string? ThumbUri { get; init; }
}

/// <summary>小程序 / 卡片消息(出站)。</summary>
public sealed record QOutgoingLightApp(string JsonPayload) : QOutgoingSegment;

/// <summary>合并转发的一条消息。</summary>
public sealed record QForwardedMessage(long UserId, string SenderName, IReadOnlyList<QOutgoingSegment> Segments)
{
    /// <summary>消息展示时间,null 使用当前时间。</summary>
    public DateTimeOffset? Time { get; init; }
}

/// <summary>合并转发。</summary>
public sealed record QOutgoingForward(IReadOnlyList<QForwardedMessage> Messages) : QOutgoingSegment
{
    public string? Title { get; init; }
    public IReadOnlyList<string>? Preview { get; init; }
    public string? Summary { get; init; }
    public string? Prompt { get; init; }
}
