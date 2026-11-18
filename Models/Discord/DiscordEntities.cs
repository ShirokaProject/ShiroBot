namespace ShiroBot.Model.Discord;

/// <summary>Discord 用户的协议无关表示。</summary>
public sealed record DiscordUser
{
    public required ulong UserId { get; init; }
    public required string Username { get; init; }
    public string? GlobalName { get; init; }
    public string? Nickname { get; init; }
    public bool IsBot { get; init; }

    /// <summary>头像 CDN 地址；用户未设置头像时为 null。</summary>
    public string? AvatarUrl { get; init; }

    public string DisplayName => Nickname ?? GlobalName ?? Username;
}

/// <summary>权限位，与 Discord API 的 permissions 字符串一一对应。</summary>
[Flags]
public enum DiscordPermissions : ulong
{
    None = 0,
    CreateInstantInvite = 1UL << 0,
    KickMembers = 1UL << 1,
    BanMembers = 1UL << 2,
    Administrator = 1UL << 3,
    ManageChannels = 1UL << 4,
    ManageGuild = 1UL << 5,
    AddReactions = 1UL << 6,
    ViewAuditLog = 1UL << 7,
    PrioritySpeaker = 1UL << 8,
    Stream = 1UL << 9,
    ViewChannel = 1UL << 10,
    SendMessages = 1UL << 11,
    SendTtsMessages = 1UL << 12,
    ManageMessages = 1UL << 13,
    EmbedLinks = 1UL << 14,
    AttachFiles = 1UL << 15,
    ReadMessageHistory = 1UL << 16,
    MentionEveryone = 1UL << 17,
    UseExternalEmojis = 1UL << 18,
    ViewGuildInsights = 1UL << 19,
    Connect = 1UL << 20,
    Speak = 1UL << 21,
    MuteMembers = 1UL << 22,
    DeafenMembers = 1UL << 23,
    MoveMembers = 1UL << 24,
    UseVoiceActivity = 1UL << 25,
    ChangeNickname = 1UL << 26,
    ManageNicknames = 1UL << 27,
    ManageRoles = 1UL << 28,
    ManageWebhooks = 1UL << 29,
    ManageGuildExpressions = 1UL << 30,
    UseApplicationCommands = 1UL << 31,
    RequestToSpeak = 1UL << 32,
    ManageEvents = 1UL << 33,
    ManageThreads = 1UL << 34,
    CreatePublicThreads = 1UL << 35,
    CreatePrivateThreads = 1UL << 36,
    UseExternalStickers = 1UL << 37,
    SendMessagesInThreads = 1UL << 38,
    UseEmbeddedActivities = 1UL << 39,
    ModerateMembers = 1UL << 40,
    ViewCreatorMonetizationAnalytics = 1UL << 41,
    UseSoundboard = 1UL << 42,
    CreateGuildExpressions = 1UL << 43,
    CreateEvents = 1UL << 44,
    UseExternalSounds = 1UL << 45,
    SendVoiceMessages = 1UL << 46,
    SetVoiceChannelStatus = 1UL << 48,
    SendPolls = 1UL << 49,
    UseExternalApps = 1UL << 50,
    PinMessages = 1UL << 51,
    BypassSlowmode = 1UL << 52
}

/// <summary>服务器。</summary>
public sealed record DiscordGuild
{
    public required ulong GuildId { get; init; }
    public required string Name { get; init; }
    public ulong OwnerId { get; init; }
    public string? IconUrl { get; init; }
    public string? Description { get; init; }

    /// <summary>近似成员数；只有查询时请求了计数才有值。</summary>
    public int? ApproximateMemberCount { get; init; }

    public int? ApproximatePresenceCount { get; init; }

    /// <summary>服务器特性，如 COMMUNITY、NEWS。</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    public string? PreferredLocale { get; init; }
}

/// <summary>频道类型，取值与 Discord API 相同。</summary>
public enum DiscordChannelType
{
    GuildText = 0,
    DirectMessage = 1,
    GuildVoice = 2,
    GroupDirectMessage = 3,
    GuildCategory = 4,
    GuildAnnouncement = 5,
    AnnouncementThread = 10,
    PublicThread = 11,
    PrivateThread = 12,
    GuildStageVoice = 13,
    GuildDirectory = 14,
    GuildForum = 15,
    GuildMedia = 16
}

/// <summary>频道、私聊或子区。SDK 频道 ID 即 <see cref="ChannelId"/>。</summary>
public sealed record DiscordChannel
{
    public required ulong ChannelId { get; init; }
    public required DiscordChannelType Type { get; init; }
    public ulong? GuildId { get; init; }
    public string? Name { get; init; }
    public string? Topic { get; init; }

    /// <summary>所属分类；子区为父频道。</summary>
    public ulong? ParentId { get; init; }

    public int? Position { get; init; }
    public bool IsNsfw { get; init; }

    /// <summary>慢速模式间隔。</summary>
    public TimeSpan? RateLimitPerUser { get; init; }

    /// <summary>子区已归档。</summary>
    public bool IsArchived { get; init; }

    public bool IsLocked { get; init; }

    public bool IsThread => Type is DiscordChannelType.AnnouncementThread or DiscordChannelType.PublicThread
        or DiscordChannelType.PrivateThread;
}

/// <summary>角色。</summary>
public sealed record DiscordRole
{
    public required ulong RoleId { get; init; }
    public required string Name { get; init; }

    /// <summary>RGB 颜色，0 表示无颜色。</summary>
    public int Color { get; init; }

    public DiscordPermissions Permissions { get; init; }
    public int Position { get; init; }

    /// <summary>在成员列表中单独分组显示。</summary>
    public bool IsHoisted { get; init; }

    public bool IsMentionable { get; init; }

    /// <summary>由集成或机器人管理，不能手动分配。</summary>
    public bool IsManaged { get; init; }
}

/// <summary>服务器成员。</summary>
public sealed record DiscordMember
{
    public required DiscordUser User { get; init; }
    public required ulong GuildId { get; init; }
    public string? Nickname { get; init; }
    public IReadOnlyList<ulong> RoleIds { get; init; } = [];
    public DateTimeOffset? JoinedAt { get; init; }

    /// <summary>超时（禁言）结束时间。</summary>
    public DateTimeOffset? TimeoutUntil { get; init; }

    /// <summary>尚未通过成员筛选。</summary>
    public bool IsPending { get; init; }

    public bool IsOwner { get; init; }

    /// <summary>按角色计算的服务器级权限；群主和管理员为全部权限。</summary>
    public DiscordPermissions Permissions { get; init; }
}

/// <summary>服务器自定义表情。</summary>
public sealed record DiscordEmoji
{
    public required ulong EmojiId { get; init; }
    public required string Name { get; init; }
    public bool IsAnimated { get; init; }
    public bool IsAvailable { get; init; } = true;

    /// <summary>消息中引用该表情的文本，如 &lt;:name:id&gt;。</summary>
    public string Mention => (IsAnimated ? "<a:" : "<:") + Name + ":" + EmojiId + ">";
}

/// <summary>邀请。</summary>
public sealed record DiscordInvite
{
    public required string Code { get; init; }
    public string Url => "https://discord.gg/" + Code;
    public ulong? GuildId { get; init; }
    public ulong? ChannelId { get; init; }
    public DiscordUser? Inviter { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int? Uses { get; init; }
    public int? MaxUses { get; init; }

    /// <summary>临时成员，离线后自动移出。</summary>
    public bool IsTemporary { get; init; }
}

/// <summary>创建邀请的选项。</summary>
public sealed record DiscordInviteOptions
{
    /// <summary>有效期，0 表示永久；默认 24 小时，最长 7 天。</summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>最多使用次数，0 表示不限。</summary>
    public int? MaxUses { get; init; }

    public bool Temporary { get; init; }

    /// <summary>不复用已有的相同设置邀请。</summary>
    public bool Unique { get; init; }
}

/// <summary>封禁记录。</summary>
public sealed record DiscordBan(DiscordUser User, string? Reason);

/// <summary>创建或修改频道的设置；null 表示不设置或不修改。</summary>
public sealed record DiscordChannelOptions
{
    public string? Name { get; init; }

    /// <summary>仅创建时生效。</summary>
    public DiscordChannelType? Type { get; init; }

    public string? Topic { get; init; }
    public ulong? ParentId { get; init; }
    public int? Position { get; init; }
    public bool? IsNsfw { get; init; }
    public TimeSpan? RateLimitPerUser { get; init; }
}

/// <summary>创建或修改角色的设置；null 表示不设置或不修改。</summary>
public sealed record DiscordRoleOptions
{
    public string? Name { get; init; }
    public DiscordPermissions? Permissions { get; init; }
    public int? Color { get; init; }
    public bool? IsHoisted { get; init; }
    public bool? IsMentionable { get; init; }
}
