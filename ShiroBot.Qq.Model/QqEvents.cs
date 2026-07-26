namespace ShiroBot.Qq.Model;

/// <summary>
/// QQ 平台特有事件负载(协议无关)。适配器把它们放进 PlatformEvent.Raw,
/// Kind 使用 <see cref="QqEventKinds"/> 中的常量;插件按 Kind 订阅后把 Raw 转成对应类型。
/// </summary>
public abstract record QqEventPayload
{
    public DateTimeOffset Time { get; init; }
    public long SelfId { get; init; }
}

/// <summary>PlatformEvent.Kind 常量表。</summary>
public static class QqEventKinds
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
public sealed record QqFriendNudge : QqEventPayload
{
    public required long UserId { get; init; }
    public bool IsSelfSend { get; init; }
    public bool IsSelfReceive { get; init; }
    public string? DisplayAction { get; init; }
    public string? DisplaySuffix { get; init; }
}

/// <summary>好友文件上传。</summary>
public sealed record QqFriendFileUpload : QqEventPayload
{
    public required long UserId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public string? FileHash { get; init; }
    public bool IsSelf { get; init; }
}

/// <summary>群管理员变更。</summary>
public sealed record QqGroupAdminChange : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public long OperatorId { get; init; }
    public bool IsSet { get; init; }
}

/// <summary>群精华消息变更。</summary>
public sealed record QqGroupEssenceMessageChange : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long MessageSeq { get; init; }
    public long OperatorId { get; init; }
    public bool IsSet { get; init; }
}

/// <summary>群名变更。</summary>
public sealed record QqGroupNameChange : QqEventPayload
{
    public required long GroupId { get; init; }
    public required string NewGroupName { get; init; }
    public long OperatorId { get; init; }
}

/// <summary>群消息表情回应。</summary>
public sealed record QqGroupMessageReaction : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public required long MessageSeq { get; init; }
    public required string FaceId { get; init; }
    public bool IsAdd { get; init; }
}

/// <summary>群禁言(Duration 为零表示解除)。</summary>
public sealed record QqGroupMute : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public long OperatorId { get; init; }
    public TimeSpan Duration { get; init; }
    public bool IsUnmute => Duration == TimeSpan.Zero;
}

/// <summary>全员禁言。</summary>
public sealed record QqGroupWholeMute : QqEventPayload
{
    public required long GroupId { get; init; }
    public long OperatorId { get; init; }
    public bool IsMute { get; init; }
}

/// <summary>群戳一戳。</summary>
public sealed record QqGroupNudge : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long SenderId { get; init; }
    public required long ReceiverId { get; init; }
    public string? DisplayAction { get; init; }
    public string? DisplaySuffix { get; init; }
}

/// <summary>群文件上传。</summary>
public sealed record QqGroupFileUpload : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long UserId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
}

/// <summary>入群申请。Token 用于 Accept/Reject。</summary>
public sealed record QqGroupJoinRequest : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long NotificationSeq { get; init; }
    public required long InitiatorId { get; init; }
    public string? Comment { get; init; }
    public bool IsFiltered { get; init; }
}

/// <summary>群成员邀请他人入群申请。</summary>
public sealed record QqGroupInvitedJoinRequest : QqEventPayload
{
    public required long GroupId { get; init; }
    public required long NotificationSeq { get; init; }
    public required long InitiatorId { get; init; }
    public required long TargetUserId { get; init; }
}

/// <summary>群解散。</summary>
public sealed record QqGroupDisband : QqEventPayload
{
    public required long GroupId { get; init; }
    public long OperatorId { get; init; }
}

/// <summary>会话置顶变更。</summary>
public sealed record QqPeerPinChange : QqEventPayload
{
    public required QqMessageScene Scene { get; init; }
    public required long PeerId { get; init; }
    public bool IsPinned { get; init; }
}
