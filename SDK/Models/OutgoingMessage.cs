namespace ShiroBot.SDK.Models;

/// <summary>Markdown 正文；原生指使用目标的 Markdown 类型，语法效果仍受平台限制。</summary>
public sealed record MarkdownSegment(string Content) : MessageSegment
{
    public string? PlainTextFallback { get; init; }
}

public sealed record CardField(string Name, string Value);
public sealed record CardSegment : MessageSegment
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<CardField> Fields { get; init; } = [];
}
public abstract record ButtonAction;
public sealed record OpenUrlAction(string Url) : ButtonAction;
public sealed record CallbackAction(string Data) : ButtonAction;
public sealed record MessageButton(string Id, string Label, ButtonAction Action);
public sealed record MessageButtonRow(IReadOnlyList<MessageButton> Buttons);
public sealed record MessageButtonLayout(IReadOnlyList<MessageButtonRow> Rows);

[Flags]
public enum MessageFallbackOptions
{
    None = 0,
    MarkdownAsText = 1,
    LinkButtonsAsText = 2,
    CardAsMarkdown = 4,
    CardAsText = 8
}

/// <summary>单条消息请求。默认禁止降级，不自动拆成多条。</summary>
public sealed record OutgoingMessage
{
    public IReadOnlyList<MessageSegment> Segments { get; init; } = [];
    public MessageButtonLayout? Buttons { get; init; }
    public MessageReference? ReplyTo { get; init; }
    public InteractionReference? ReplyToInteraction { get; init; }
    public MessageFallbackOptions AllowedFallbacks { get; init; }
}

[Flags]
public enum MessageFeatures
{
    None = 0, Text = 1, Markdown = 2, Card = 4, Image = 8, Audio = 16, Video = 32,
    File = 64, Mention = 128, MentionAll = 256, Emoji = 512, Quote = 1024,
    LinkButtons = 2048, CallbackButtons = 4096, Raw = 8192, InteractionReply = 16384
}
/// <summary>目标的适配器映射能力，不代表账号已经获得平台授权。</summary>
public sealed record MessageCapabilities
{
    public MessageFeatures NativeFeatures { get; init; }
    public bool CanMixMarkdown { get; init; }
    public bool ButtonsRequireMarkdown { get; init; }
    public bool CanCombineButtonsWithMedia { get; init; }
    public int? MaxButtonRows { get; init; }
    public int? MaxButtonsPerRow { get; init; }
    public int? MaxCallbackDataBytes { get; init; }
    public int? MaxMediaSegments { get; init; }
    public int? MaxTextLength { get; init; }
}
public enum MessageTransformationKind { MarkdownToText, LinkButtonsToText, CardToMarkdown, CardToText, TextToMarkdown }
public sealed record MessageTransformation(MessageTransformationKind Kind, bool IsDowngrade);
public sealed record MessageSendAssessment
{
    public bool IsSupported { get; init; }
    public bool IsNative => IsSupported && Transformations.All(x => !x.IsDowngrade);
    public IReadOnlyList<MessageTransformation> Transformations { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
}
public sealed record PreparedMessage(OutgoingMessage Message, IReadOnlyList<MessageTransformation> Transformations);
