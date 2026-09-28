namespace ShiroBot.Model.QQ;

/// <summary>QQ 官方机器人消息目标。单聊/群聊 Id 是开放平台 openid；频道使用对应频道或私信会话 ID。</summary>
public sealed record QOfficialMessageTarget(QOfficialMessageScene Scene, string Id);

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
