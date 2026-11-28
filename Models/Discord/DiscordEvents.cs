using System.Text.Json;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Discord;

/// <summary>
///     Discord 特有事件。适配器以 <see cref="PlatformEvent"/> 上报：<c>Kind</c> 为 Gateway 事件名的小写形式，
///     下列常量的 <c>Raw</c> 为对应负载，其他事件的 <c>Raw</c> 为 <see cref="JsonElement"/>。
///     消息、删除、成员加入/离开、交互和 Reaction 照常映射为 SDK 事件。
/// </summary>
public static class DiscordEventKinds
{
    /// <summary>服务器可用或机器人加入服务器；<see cref="DiscordGuildAvailable"/>。</summary>
    public const string GuildCreate = "guild_create";

    /// <summary>服务器不可用或机器人被移出；<see cref="DiscordGuildUnavailable"/>。</summary>
    public const string GuildDelete = "guild_delete";

    public const string GuildUpdate = "guild_update";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ChannelCreate = "channel_create";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ChannelUpdate = "channel_update";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ChannelDelete = "channel_delete";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ThreadCreate = "thread_create";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ThreadUpdate = "thread_update";

    /// <summary><see cref="DiscordChannelChanged"/>。</summary>
    public const string ThreadDelete = "thread_delete";

    /// <summary>成员昵称、角色或超时变化；<see cref="DiscordMemberUpdated"/>。</summary>
    public const string GuildMemberUpdate = "guild_member_update";

    /// <summary><see cref="DiscordRoleChanged"/>。</summary>
    public const string GuildRoleCreate = "guild_role_create";

    /// <summary><see cref="DiscordRoleChanged"/>。</summary>
    public const string GuildRoleUpdate = "guild_role_update";

    /// <summary><see cref="DiscordRoleChanged"/>，<c>Role</c> 只有 ID。</summary>
    public const string GuildRoleDelete = "guild_role_delete";

    /// <summary><see cref="DiscordBanChanged"/>。</summary>
    public const string GuildBanAdd = "guild_ban_add";

    /// <summary><see cref="DiscordBanChanged"/>。</summary>
    public const string GuildBanRemove = "guild_ban_remove";

    /// <summary>加入、离开或切换语音频道，静音状态变化；<see cref="DiscordVoiceStateUpdated"/>。</summary>
    public const string VoiceStateUpdate = "voice_state_update";

    /// <summary>需要 Typing Intent；<see cref="DiscordTypingStarted"/>。</summary>
    public const string TypingStart = "typing_start";

    /// <summary><see cref="DiscordReactionsCleared"/>。</summary>
    public const string MessageReactionRemoveAll = "message_reaction_remove_all";

    /// <summary><see cref="DiscordReactionsCleared"/>，带 <c>Emoji</c>。</summary>
    public const string MessageReactionRemoveEmoji = "message_reaction_remove_emoji";

    /// <summary><see cref="DiscordPollVote"/>。</summary>
    public const string MessagePollVoteAdd = "message_poll_vote_add";

    /// <summary><see cref="DiscordPollVote"/>。</summary>
    public const string MessagePollVoteRemove = "message_poll_vote_remove";

    /// <summary><see cref="DiscordInviteChanged"/>。</summary>
    public const string InviteCreate = "invite_create";

    /// <summary><see cref="DiscordInviteChanged"/>。</summary>
    public const string InviteDelete = "invite_delete";
}

/// <summary>Discord 事件负载的公共字段。</summary>
public abstract record DiscordEventPayload
{
    public ulong? GuildId { get; init; }

    /// <summary>Gateway 原始 data。</summary>
    public JsonElement Json { get; init; }
}

public sealed record DiscordGuildAvailable : DiscordEventPayload
{
    public required DiscordGuild Guild { get; init; }

    /// <summary>机器人加入的时间。</summary>
    public DateTimeOffset? JoinedAt { get; init; }

    public int MemberCount { get; init; }
    public IReadOnlyList<DiscordChannel> Channels { get; init; } = [];
    public IReadOnlyList<DiscordRole> Roles { get; init; } = [];
}

public sealed record DiscordGuildUnavailable : DiscordEventPayload
{
    /// <summary>true 为服务器暂时故障；false 为机器人被移出或服务器已删除。</summary>
    public bool IsOutage { get; init; }
}

public sealed record DiscordChannelChanged : DiscordEventPayload
{
    public required DiscordChannel Channel { get; init; }
}

public sealed record DiscordMemberUpdated : DiscordEventPayload
{
    public required DiscordMember Member { get; init; }
}

public sealed record DiscordRoleChanged : DiscordEventPayload
{
    public required ulong RoleId { get; init; }

    /// <summary>删除事件中为 null。</summary>
    public DiscordRole? Role { get; init; }
}

public sealed record DiscordBanChanged : DiscordEventPayload
{
    public required DiscordUser User { get; init; }
}

public sealed record DiscordVoiceStateUpdated : DiscordEventPayload
{
    public required ulong UserId { get; init; }

    /// <summary>离开语音时为 null。</summary>
    public ulong? ChannelId { get; init; }

    public DiscordMember? Member { get; init; }
    public bool IsMuted { get; init; }
    public bool IsDeafened { get; init; }
    public bool IsSelfMuted { get; init; }
    public bool IsSelfDeafened { get; init; }
    public bool IsStreaming { get; init; }
    public bool IsVideoOn { get; init; }
}

public sealed record DiscordTypingStarted : DiscordEventPayload
{
    public required ulong ChannelId { get; init; }
    public required ulong UserId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public sealed record DiscordReactionsCleared : DiscordEventPayload
{
    public required ulong ChannelId { get; init; }
    public required ulong MessageId { get; init; }

    /// <summary>只清除某个表情时提供；Unicode 表情为字符，自定义表情为 name:id。</summary>
    public string? Emoji { get; init; }
}

public sealed record DiscordPollVote : DiscordEventPayload
{
    public required ulong UserId { get; init; }
    public required ulong ChannelId { get; init; }
    public required ulong MessageId { get; init; }
    public int AnswerId { get; init; }
    public bool IsAdded { get; init; }
}

public sealed record DiscordInviteChanged : DiscordEventPayload
{
    public required string Code { get; init; }
    public ulong ChannelId { get; init; }

    /// <summary>删除事件中为 null。</summary>
    public DiscordInvite? Invite { get; init; }
}
