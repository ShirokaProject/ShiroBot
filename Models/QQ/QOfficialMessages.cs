namespace ShiroBot.Model.QQ;

/// <summary>QQ 官方机器人消息目标。单聊/群聊 Id 是开放平台 openid；频道 Id 是 channel_id；频道私信 Id 是 guild_id。</summary>
public sealed record QOfficialMessageTarget(QOfficialMessageScene Scene, string Id);

/// <summary>一条 QQ 官方消息。根据消息种类限制可组合的正文和键盘。</summary>
public abstract record QOfficialMessage
{
    public static QOfficialMessage Text(string content) => new QOfficialTextMessage(content);

    public static QOfficialMessage Markdown(QOfficialMarkdown markdown, QOfficialKeyboard? keyboard = null) =>
        new QOfficialMarkdownMessage(markdown, keyboard);

    /// <summary>创建本地媒体消息；媒体上传由适配器在发送时完成。</summary>
    public static QOfficialMessage Media(QOfficialMediaType type, Stream content, string fileName,
        string? caption = null) => new QOfficialMediaSourceMessage(type, content, fileName, caption);

    /// <summary>创建已上传媒体消息，可在被动回复时复用 file_info。</summary>
    public static QOfficialMessage Media(QOfficialMedia media, string? caption = null) =>
        new QOfficialUploadedMediaMessage(media, caption);
}

public sealed record QOfficialTextMessage(string Content) : QOfficialMessage;

public sealed record QOfficialMarkdownMessage(QOfficialMarkdown Content, QOfficialKeyboard? Keyboard = null)
    : QOfficialMessage;

public sealed record QOfficialMediaSourceMessage(
    QOfficialMediaType Type,
    Stream Content,
    string FileName,
    string? Caption = null) : QOfficialMessage;

public sealed record QOfficialUploadedMediaMessage(QOfficialMedia UploadedMedia, string? Caption = null)
    : QOfficialMessage;

public enum QOfficialMessageScene
{
    Direct = 0,
    Group = 1,
    Channel = 2,
    ChannelDirect = 3
}

/// <summary>QQ 官方 Markdown 消息。发送时在自定义内容和模板之间选择一种。</summary>
public abstract record QOfficialMarkdown;

public sealed record QCustomMarkdown(string Content) : QOfficialMarkdown;

public sealed record QTemplateMarkdown(
    string CustomTemplateId,
    IReadOnlyList<QMarkdownParameter> Params) : QOfficialMarkdown;

public sealed record QMarkdownParameter(string Key, IReadOnlyList<string> Values);

/// <summary>QQ 官方 Embed 卡片。字段格式按官方 Embed 消息模型定义。</summary>
public sealed record QOfficialEmbed
{
    public string? Title { get; init; }
    public string? Prompt { get; init; }
    public QOfficialEmbedThumbnail? Thumbnail { get; init; }
    public IReadOnlyList<QOfficialEmbedField>? Fields { get; init; }
}

public sealed record QOfficialEmbedThumbnail(string Url);

public sealed record QOfficialEmbedField(string Name);

/// <summary>QQ 官方按钮键盘。模板 ID 和自定义按钮内容互斥。</summary>
public abstract record QOfficialKeyboard;

public sealed record QKeyboardTemplate(string Id) : QOfficialKeyboard;

/// <summary>自定义按钮最多五行，每行最多五个按钮；需要开放平台开通相应能力。</summary>
public sealed record QInlineKeyboard(IReadOnlyList<QKeyboardRow> Rows) : QOfficialKeyboard;

public sealed record QKeyboardRow(IReadOnlyList<QKeyboardButton> Buttons);

public sealed record QKeyboardButton
{
    /// <summary>在同一键盘内唯一。</summary>
    public string? Id { get; init; }
    public required QKeyboardRenderData RenderData { get; init; }
    public required QKeyboardAction Action { get; init; }
}

public sealed record QKeyboardRenderData(string Label, string VisitedLabel, QKeyboardButtonStyle Style);

public enum QKeyboardButtonStyle
{
    Gray = 0,
    Blue = 1
}

public sealed record QKeyboardAction
{
    public required QKeyboardActionType Type { get; init; }
    public required QKeyboardPermission Permission { get; init; }
    /// <summary>跳转 URL、回调数据或指令内容，依 Type 而定。</summary>
    public required string Data { get; init; }
    public required string UnsupportTips { get; init; }
    /// <summary>仅指令按钮有效；是否引用本消息。</summary>
    public bool? Reply { get; init; }
    /// <summary>仅指令按钮有效；点击后直接发送指令。</summary>
    public bool? Enter { get; init; }
    /// <summary>仅指令按钮有效；设置后忽略 Enter。值 1 唤起手机端选图器。</summary>
    public int? Anchor { get; init; }
}

public enum QKeyboardActionType
{
    Jump = 0,
    Callback = 1,
    Command = 2
}

public sealed record QKeyboardPermission
{
    public required QKeyboardPermissionType Type { get; init; }
    public IReadOnlyList<string>? SpecifyUserIds { get; init; }
    /// <summary>仅频道的指定身份组权限有效。</summary>
    public IReadOnlyList<string>? SpecifyRoleIds { get; init; }
}

public enum QKeyboardPermissionType
{
    SpecifiedUsers = 0,
    Managers = 1,
    Everyone = 2,
    SpecifiedRoles = 3
}

/// <summary>被动回复信息。主动发送时留空。</summary>
public sealed record QOfficialMessageReply
{
    public string? MessageId { get; init; }
    public string? EventId { get; init; }
    public int? MessageSequence { get; init; }
}

/// <summary>QQ 官方群媒体类型。</summary>
public enum QOfficialMediaType
{
    Image = 1,
    Video = 2,
    Audio = 3,
    File = 4
}

/// <summary>已上传到 QQ 官方平台的媒体引用，可用于后续发送。</summary>
public sealed record QOfficialMedia(string FileInfo, QOfficialMediaType Type, string FileName);

public enum QOfficialStreamContentType
{
    Text = 0,
    Markdown = 1
}

/// <summary>QQ 官方互动事件响应码。</summary>
public enum QOfficialInteractionResponseCode
{
    Success = 0,
    Failed = 1,
    TooFrequent = 2,
    Duplicate = 3,
    NoPermission = 4,
    ManagersOnly = 5
}
