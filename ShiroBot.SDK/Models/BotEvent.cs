namespace ShiroBot.SDK.Models;

/// <summary>
/// 平台无关的事件基类。所有适配器上报的事件都必须继承此类型。
/// </summary>
public abstract record BotEvent
{
    /// <summary>产生事件的平台 ID（如 "qq"、"discord"、"telegram"），与适配器的 Platform 一致。</summary>
    public required string Platform { get; init; }

    /// <summary>收到事件的机器人自身账号 ID。</summary>
    public string? SelfId { get; init; }

    /// <summary>
    /// 平台原始事件对象。需要访问平台特有字段的插件可按平台向下转型。
    /// </summary>
    public object? Raw { get; init; }
}

/// <summary>消息被撤回 / 删除。</summary>
public sealed record MessageDeletedEvent : BotEvent
{
    public required string MessageId { get; init; }
    public required Channel Channel { get; init; }

    /// <summary>被撤回消息的发送者 ID，未知时为 null。</summary>
    public string? SenderId { get; init; }

    /// <summary>执行撤回操作的用户 ID，未知时为 null。</summary>
    public string? OperatorId { get; init; }
}

/// <summary>成员加入群 / 频道。</summary>
public sealed record MemberJoinedEvent : BotEvent
{
    public required Channel Channel { get; init; }
    public required string UserId { get; init; }

    /// <summary>邀请人 / 操作者 ID，未知时为 null。</summary>
    public string? OperatorId { get; init; }
}

/// <summary>成员离开群 / 频道（主动退出或被移除）。</summary>
public sealed record MemberLeftEvent : BotEvent
{
    public required Channel Channel { get; init; }
    public required string UserId { get; init; }

    /// <summary>执行移除的操作者 ID；主动退出时为 null。</summary>
    public string? OperatorId { get; init; }
}

/// <summary>好友请求 / 私聊申请。</summary>
public sealed record FriendRequestEvent : BotEvent
{
    public required string UserId { get; init; }

    /// <summary>验证信息 / 附言。</summary>
    public string? Comment { get; init; }

    /// <summary>用于接受 / 拒绝该请求的平台令牌。</summary>
    public string? Token { get; init; }
}

/// <summary>机器人被邀请加入群 / 服务器。</summary>
public sealed record GuildInviteEvent : BotEvent
{
    public required string GuildId { get; init; }
    public required string InviterId { get; init; }

    /// <summary>用于接受 / 拒绝该邀请的平台令牌。</summary>
    public string? Token { get; init; }
}

/// <summary>机器人离线 / 连接断开。</summary>
public sealed record BotOfflineEvent : BotEvent
{
    public string? Reason { get; init; }
}

/// <summary>
/// 平台特有事件（戳一戳、精华消息、Reaction 等通用模型未覆盖的事件）。
/// 插件按 <see cref="Kind"/> 识别并读取 <see cref="BotEvent.Raw"/>。
/// </summary>
public sealed record PlatformEvent : BotEvent
{
    /// <summary>平台事件类型标识（适配器约定，如 "group_nudge"、"reaction_add"）。</summary>
    public required string Kind { get; init; }

    /// <summary>事件关联的渠道，无渠道语义时为 null。</summary>
    public Channel? Channel { get; init; }
}
