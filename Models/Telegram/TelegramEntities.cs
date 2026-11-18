namespace ShiroBot.Model.Telegram;

/// <summary>Telegram 用户的协议无关表示。</summary>
public sealed record TelegramUser
{
    public required long UserId { get; init; }
    public required string FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public bool IsBot { get; init; }

    /// <summary>用户客户端的 IETF 语言标签，如 <c>zh-hans</c>；只在用户与机器人交互时提供。</summary>
    public string? LanguageCode { get; init; }

    public bool IsPremium { get; init; }

    public string DisplayName => string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

/// <summary>Telegram 会话类型。</summary>
public enum TelegramChatType
{
    Private,
    Group,
    Supergroup,
    Channel
}

/// <summary>Telegram 会话。查询结果（<see cref="ITelegramChatApi.GetChatAsync"/>）会填充资料字段，事件中通常只有基础字段。</summary>
public sealed record TelegramChat
{
    public required long ChatId { get; init; }
    public required TelegramChatType Type { get; init; }

    /// <summary>群、超级群组和频道的标题。</summary>
    public string? Title { get; init; }

    public string? Username { get; init; }

    /// <summary>私聊对方的名字。</summary>
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>超级群组已启用话题。</summary>
    public bool IsForum { get; init; }

    /// <summary>频道的私信会话。</summary>
    public bool IsDirectMessages { get; init; }

    public string? Description { get; init; }

    /// <summary>私聊对方的简介。</summary>
    public string? Bio { get; init; }

    /// <summary>主邀请链接；机器人需要相应管理员权限才能看到。</summary>
    public string? InviteLink { get; init; }

    /// <summary>群组成员的默认权限。</summary>
    public TelegramChatPermissions? Permissions { get; init; }

    /// <summary>慢速模式间隔。</summary>
    public TimeSpan? SlowModeDelay { get; init; }

    /// <summary>频道的讨论组，或讨论组所属的频道。</summary>
    public long? LinkedChatId { get; init; }

    public string DisplayName => Title ?? string.Join(" ",
        new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

/// <summary>成员在会话中的状态。</summary>
public enum TelegramMemberStatus
{
    /// <summary>群主（Bot API 的 <c>creator</c>）。</summary>
    Owner,
    Administrator,
    Member,

    /// <summary>受限成员；<see cref="TelegramChatMember.IsMember"/> 表示其是否仍在会话中。</summary>
    Restricted,
    Left,

    /// <summary>已封禁（Bot API 的 <c>kicked</c>）。</summary>
    Banned
}

/// <summary>会话成员及其权限。</summary>
public sealed record TelegramChatMember
{
    public required TelegramUser User { get; init; }
    public required TelegramMemberStatus Status { get; init; }

    /// <summary>群主或管理员的自定义头衔。</summary>
    public string? CustomTitle { get; init; }

    public bool IsAnonymous { get; init; }

    /// <summary>受限成员是否仍在会话中。</summary>
    public bool IsMember { get; init; }

    /// <summary>限制或封禁的结束时间；null 表示永久。</summary>
    public DateTimeOffset? UntilDate { get; init; }

    /// <summary>受限成员的权限。</summary>
    public TelegramChatPermissions? Permissions { get; init; }

    /// <summary>管理员权限；群主拥有全部权限，此处为 null。</summary>
    public TelegramAdminRights? AdminRights { get; init; }

    /// <summary>机器人能否修改该管理员的权限。</summary>
    public bool CanBeEdited { get; init; }
}

/// <summary>成员权限。null 表示不修改（设置时）或平台未返回（读取时）。</summary>
public sealed record TelegramChatPermissions
{
    public bool? CanSendMessages { get; init; }
    public bool? CanSendAudios { get; init; }
    public bool? CanSendDocuments { get; init; }
    public bool? CanSendPhotos { get; init; }
    public bool? CanSendVideos { get; init; }
    public bool? CanSendVideoNotes { get; init; }
    public bool? CanSendVoiceNotes { get; init; }
    public bool? CanSendPolls { get; init; }

    /// <summary>贴纸、GIF、游戏和 inline 机器人消息。</summary>
    public bool? CanSendOtherMessages { get; init; }

    public bool? CanAddWebPagePreviews { get; init; }
    public bool? CanReactToMessages { get; init; }
    public bool? CanChangeInfo { get; init; }
    public bool? CanInviteUsers { get; init; }
    public bool? CanPinMessages { get; init; }
    public bool? CanManageTopics { get; init; }

    /// <summary>禁止发送任何内容。</summary>
    public static TelegramChatPermissions ReadOnly { get; } = new()
    {
        CanSendMessages = false, CanSendAudios = false, CanSendDocuments = false, CanSendPhotos = false,
        CanSendVideos = false, CanSendVideoNotes = false, CanSendVoiceNotes = false, CanSendPolls = false,
        CanSendOtherMessages = false, CanAddWebPagePreviews = false, CanReactToMessages = false
    };
}

/// <summary>管理员权限。提升成员时，未设置的项视为 false。</summary>
public sealed record TelegramAdminRights
{
    public bool IsAnonymous { get; init; }
    public bool CanManageChat { get; init; }
    public bool CanDeleteMessages { get; init; }
    public bool CanManageVideoChats { get; init; }
    public bool CanRestrictMembers { get; init; }
    public bool CanPromoteMembers { get; init; }
    public bool CanChangeInfo { get; init; }
    public bool CanInviteUsers { get; init; }
    public bool CanPostStories { get; init; }
    public bool CanEditStories { get; init; }
    public bool CanDeleteStories { get; init; }

    /// <summary>仅频道。</summary>
    public bool CanPostMessages { get; init; }

    /// <summary>仅频道。</summary>
    public bool CanEditMessages { get; init; }

    public bool CanPinMessages { get; init; }

    /// <summary>仅超级群组。</summary>
    public bool CanManageTopics { get; init; }

    public bool CanManageDirectMessages { get; init; }
}

/// <summary>超级群组的话题。SDK 频道 ID 为 <c>chatId:ThreadId</c>。</summary>
public sealed record TelegramForumTopic
{
    public required long ThreadId { get; init; }
    public required string Name { get; init; }

    /// <summary>RGB 图标颜色。</summary>
    public int IconColor { get; init; }

    public string? IconCustomEmojiId { get; init; }
}

/// <summary>邀请链接。</summary>
public sealed record TelegramInviteLink
{
    public required string Link { get; init; }
    public string? Name { get; init; }
    public TelegramUser? Creator { get; init; }

    /// <summary>通过该链接加入需要管理员审批，产生 <see cref="TelegramEventKinds.ChatJoinRequest"/> 事件。</summary>
    public bool CreatesJoinRequest { get; init; }

    public bool IsPrimary { get; init; }
    public bool IsRevoked { get; init; }
    public DateTimeOffset? ExpireDate { get; init; }
    public int? MemberLimit { get; init; }
    public int? PendingJoinRequestCount { get; init; }
}

/// <summary>创建或修改邀请链接的选项。</summary>
public sealed record TelegramInviteLinkOptions
{
    public string? Name { get; init; }
    public DateTimeOffset? ExpireDate { get; init; }

    /// <summary>1–99999；与 <see cref="CreatesJoinRequest"/> 互斥。</summary>
    public int? MemberLimit { get; init; }

    public bool CreatesJoinRequest { get; init; }
}

/// <summary>机器人命令，显示在客户端的命令菜单中。</summary>
/// <param name="Command">1–32 个小写字母、数字或下划线，不带 /。</param>
/// <param name="Description">1–256 个字符。</param>
public sealed record TelegramBotCommand(string Command, string Description);

public enum TelegramCommandScopeKind
{
    Default,
    AllPrivateChats,
    AllGroupChats,
    AllChatAdministrators,

    /// <summary>需要 <see cref="TelegramCommandScope.ChatId"/>。</summary>
    Chat,

    /// <summary>需要 <see cref="TelegramCommandScope.ChatId"/>。</summary>
    ChatAdministrators,

    /// <summary>需要 <see cref="TelegramCommandScope.ChatId"/> 和 <see cref="TelegramCommandScope.UserId"/>。</summary>
    ChatMember
}

/// <summary>命令的生效范围。</summary>
public sealed record TelegramCommandScope(TelegramCommandScopeKind Kind)
{
    /// <summary>会话 ID，数字或 <c>@username</c>。</summary>
    public string? ChatId { get; init; }

    public string? UserId { get; init; }

    public static TelegramCommandScope Default { get; } = new(TelegramCommandScopeKind.Default);
}
