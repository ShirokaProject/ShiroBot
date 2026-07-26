namespace ShiroBot.SDK.Models;

/// <summary>
/// 平台无关的消息段基类。适配器无法映射到通用段的内容应使用 <see cref="RawSegment"/>。
/// </summary>
public abstract record MessageSegment;

/// <summary>纯文本。</summary>
public sealed record TextSegment(string Text) : MessageSegment
{
    public override string ToString() => Text;
}

/// <summary>提及（@）某个用户。</summary>
public sealed record MentionSegment(string UserId) : MessageSegment
{
    /// <summary>被提及用户的显示名，用于平台不支持原生 @ 时的降级渲染。</summary>
    public string? DisplayName { get; init; }
}

/// <summary>提及全体成员。</summary>
public sealed record MentionAllSegment : MessageSegment;

/// <summary>引用回复某条消息。</summary>
public sealed record QuoteSegment(string MessageId) : MessageSegment;

/// <summary>表情。<see cref="Id"/> 为平台表情 ID（QQ face id、Discord emoji id 等）。</summary>
public sealed record EmojiSegment(string Id) : MessageSegment
{
    /// <summary>Unicode 或 :name: 形式的降级文本。</summary>
    public string? Name { get; init; }
}

/// <summary>媒体资源段的公共基类。发送时 <see cref="Uri"/> 支持 http(s)、file、base64 等适配器约定的形式。</summary>
public abstract record ResourceSegment(string Uri) : MessageSegment
{
    /// <summary>文件名，未知时为 null。</summary>
    public string? FileName { get; init; }

    /// <summary>平台内部资源 ID（接收时可用于二次拉取）。</summary>
    public string? ResourceId { get; init; }
}

/// <summary>图片。</summary>
public sealed record ImageSegment(string Uri) : ResourceSegment(Uri)
{
    public int? Width { get; init; }
    public int? Height { get; init; }

    /// <summary>图片摘要 / alt 文本。</summary>
    public string? Summary { get; init; }
}

/// <summary>语音 / 音频。</summary>
public sealed record AudioSegment(string Uri) : ResourceSegment(Uri)
{
    public TimeSpan? Duration { get; init; }
}

/// <summary>视频。</summary>
public sealed record VideoSegment(string Uri) : ResourceSegment(Uri);

/// <summary>文件。</summary>
public sealed record FileSegment(string Uri) : ResourceSegment(Uri)
{
    public long? FileSize { get; init; }
}

/// <summary>
/// 平台特有消息段。通用模型无法表达的内容由适配器以自定义负载形式保留，
/// 插件可按 <see cref="Platform"/> + <see cref="Kind"/> 识别并读取 <see cref="Payload"/>。
/// </summary>
public sealed record RawSegment(string Platform, string Kind, object Payload) : MessageSegment;
