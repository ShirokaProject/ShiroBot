namespace ShiroBot.SDK.Models;

/// <summary>
/// 会话渠道类型。
/// </summary>
public enum ChannelType
{
    /// <summary>私聊 / DM。</summary>
    Direct = 0,

    /// <summary>群聊（QQ 群、Telegram group、Discord text channel）。</summary>
    Group = 1,

    /// <summary>子话题 / Thread（Discord thread、Telegram topic）。</summary>
    Thread = 2,

    /// <summary>其他平台特有会话（如 QQ 临时会话）。</summary>
    Other = 3
}

/// <summary>
/// 平台无关的会话渠道。私聊时 <see cref="Id"/> 通常为对方用户 ID；
/// 群聊时为群 / 频道 ID。
/// </summary>
public sealed record Channel(string Id, ChannelType Type)
{
    /// <summary>渠道显示名称（群名 / 频道名），未知时为 null。</summary>
    public string? Name { get; init; }

    /// <summary>所属服务器 / 群组 ID。QQ 群与群渠道相同；Discord 为 guild id。</summary>
    public string? GuildId { get; init; }

    public static Channel Direct(string userId) => new(userId, ChannelType.Direct);

    public static Channel Group(string groupId) => new(groupId, ChannelType.Group) { GuildId = groupId };
}
