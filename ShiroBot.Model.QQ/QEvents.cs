namespace ShiroBot.Model.QQ;

/// <summary>
/// QQ 平台特有事件负载(协议无关)。适配器把它们放进 PlatformEvent.Raw,
/// Kind 使用 <see cref="QEventKinds"/> 中的常量;插件按 Kind 订阅后把 Raw 转成对应类型。
/// </summary>
public abstract record QEventPayload
{
    public DateTimeOffset Time { get; init; }
    public long SelfId { get; init; }
}

/// <summary>PlatformEvent.Kind 常量表。</summary>
public static class QEventKinds
{
    public const string FriendNudge = "friend_nudge";
    public const string FriendFileUpload = "friend_file_upload";
    public const string GroupAdminChange = "group_admin_change";
    public const string GroupEssenceMessageChange = "group_essence_message_change";
    public const string GroupNameChange = "group_name_change";
    public const string GroupMessageReaction = "group_message_reaction";
    public const string GroupMute = "group_mute";
    public const string GroupWholeMute = "group_whole_mute";
    public const string GroupNudge = "group_nudge";
    public const string GroupFileUpload = "group_file_upload";
    public const string GroupJoinRequest = "group_join_request";
    public const string GroupInvitedJoinRequest = "group_invited_join_request";
    public const string GroupDisband = "group_disband";
    public const string PeerPinChange = "peer_pin_change";
}

/// <summary>好友戳一戳。</summary>
public sealed record QFriendNudge : QEventPayload
{
    public required long UserId { get; init; }
    public bool IsSelfSend { get; init; }
    public bool IsSelfReceive { get; init; }
    public string? DisplayAction { get; init; }
    public string? DisplaySuffix { get; init; }

    /// <summary>戳一戳动作图片 URL(存在时用于取代动作提示文本)。</summary>
    public string? DisplayActionImgUrl { get; init; }
}

/// <summary>好友文件上传。</summary>
public sealed record QFriendFileUpload : QEventPayload
{
    public required long UserId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public string? FileHash { get; init; }
    public bool IsSelf { get; init; }
}

/// <summary>群管理员变更。</summary>
public sealed record QGroupAdminChange : QEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public long OperatorId { get; init; }
    public bool IsSet { get; init; }
}

/// <summary>群精华消息变更。</summary>
public sealed record QGroupEssenceMessageChange : QEventPayload
{
    public required long GroupId { get; init; }
    public required long MessageSeq { get; init; }
    public long OperatorId { get; init; }
    public bool IsSet { get; init; }
}

/// <summary>群名变更。</summary>
public sealed record QGroupNameChange : QEventPayload
{
    public required long GroupId { get; init; }
    public required string NewGroupName { get; init; }
    public long OperatorId { get; init; }
}

/// <summary>群消息表情回应。</summary>
public sealed record QGroupMessageReaction : QEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public required long MessageSeq { get; init; }
    public required string FaceId { get; init; }

    /// <summary>回应类型(QQ 表情 / Emoji 字符)。</summary>
    public QReactionType ReactionType { get; init; }

    public bool IsAdd { get; init; }
}

/// <summary>群禁言(Duration 为零表示解除)。</summary>
public sealed record QGroupMute : QEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public long OperatorId { get; init; }
    public TimeSpan Duration { get; init; }
    public bool IsUnmute => Duration == TimeSpan.Zero;
}

/// <summary>全员禁言。</summary>
public sealed record QGroupWholeMute : QEventPayload
{
    public required long GroupId { get; init; }
    public long OperatorId { get; init; }
    public bool IsMute { get; init; }
}

/// <summary>群戳一戳。</summary>
public sealed record QGroupNudge : QEventPayload
{
    public required long GroupId { get; init; }
    public required long SenderId { get; init; }
    public required long ReceiverId { get; init; }
    public string? DisplayAction { get; init; }
    public string? DisplaySuffix { get; init; }

    /// <summary>戳一戳动作图片 URL(存在时用于取代动作提示文本)。</summary>
    public string? DisplayActionImgUrl { get; init; }
}

/// <summary>群文件上传。</summary>
public sealed record QGroupFileUpload : QEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
}

/// <summary>入群申请。</summary>
public sealed record QGroupJoinRequest : QEventPayload
{
    public required long GroupId { get; init; }
    public required long NotificationSeq { get; init; }
    public required long InitiatorId { get; init; }
    public string? Comment { get; init; }
    public bool IsFiltered { get; init; }

    /// <summary>协议处理令牌(OneBot 为 flag;Milky 不使用)。</summary>
    public string? Token { get; init; }
}

/// <summary>群成员邀请他人入群申请。</summary>
public sealed record QGroupInvitedJoinRequest : QEventPayload
{
    public required long GroupId { get; init; }
    public required long NotificationSeq { get; init; }
    public required long InitiatorId { get; init; }
    public required long TargetUserId { get; init; }

    /// <summary>协议处理令牌(OneBot 为 flag;Milky 不使用)。</summary>
    public string? Token { get; init; }
}

/// <summary>群解散。</summary>
public sealed record QGroupDisband : QEventPayload
{
    public required long GroupId { get; init; }
    public long OperatorId { get; init; }
}

/// <summary>会话置顶变更。</summary>
public sealed record QPeerPinChange : QEventPayload
{
    public required QMessageScene Scene { get; init; }
    public required long PeerId { get; init; }
    public bool IsPinned { get; init; }
}

/// <summary>消息撤回的 QQ 原始信息。</summary>
public sealed record QMessageRecall : QEventPayload
{
    public required QMessageScene Scene { get; init; }
    public required long PeerId { get; init; }
    public required long MessageSeq { get; init; }
    public required long SenderId { get; init; }
    public required long OperatorId { get; init; }
    public string? DisplaySuffix { get; init; }
}

/// <summary>群成员增加的 QQ 原始信息。</summary>
public sealed record QGroupMemberIncrease : QEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public long? OperatorId { get; init; }
    public long? InvitorId { get; init; }
}

/// <summary>群成员减少的 QQ 原始信息。</summary>
public sealed record QGroupMemberDecrease : QEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public long? OperatorId { get; init; }
}

/// <summary>好友请求的 QQ 原始信息。</summary>
public sealed record QFriendRequestReceived : QEventPayload
{
    public required long InitiatorId { get; init; }
    public required string InitiatorUid { get; init; }
    public string? Comment { get; init; }
    public string? Via { get; init; }
}

/// <summary>机器人被邀请入群的 QQ 原始信息。</summary>
public sealed record QGroupInvitation : QEventPayload
{
    public required long GroupId { get; init; }
    public required long InvitationSeq { get; init; }
    public required long InitiatorId { get; init; }
    public long? SourceGroupId { get; init; }
}

/// <summary>机器人离线的 QQ 原始信息。</summary>
public sealed record QBotOffline : QEventPayload
{
    public string? Reason { get; init; }
}
