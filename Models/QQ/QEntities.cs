namespace ShiroBot.Model.QQ;

// ID 均为适配器作用域内的不透明字符串：Milky 使用数字字符串，官方使用 OpenID。
// 不要在插件中把 ID 转成整数或跨适配器比较。文件大小、时间、计数保持数值类型。

public enum QSex
{
    Unknown = 0,
    Male = 1,
    Female = 2
}

public enum QGroupRole
{
    Unknown = 0,
    Member = 1,
    Admin = 2,
    Owner = 3
}

/// <summary>好友分组。</summary>
public sealed record QFriendCategory(int CategoryId, string CategoryName);

/// <summary>好友。</summary>
public sealed record QFriend
{
    public required string UserId { get; init; }
    public string? Nickname { get; init; }
    public QSex Sex { get; init; }
    public string? Qid { get; init; }
    public string? Remark { get; init; }
    public QFriendCategory? Category { get; init; }
}

/// <summary>群。</summary>
public sealed record QGroup
{
    public required string GroupId { get; init; }
    public string? GroupName { get; init; }
    public int? MemberCount { get; init; }
    public int? MaxMemberCount { get; init; }
    public string? Remark { get; init; }
    public DateTimeOffset? CreatedTime { get; init; }
    public string? Description { get; init; }
    public string? Announcement { get; init; }

    /// <summary>入群验证问题(群主设置的问题,用于验证加入请求)。</summary>
    public string? Question { get; init; }
}

/// <summary>群成员。</summary>
public sealed record QGroupMember
{
    public required string UserId { get; init; }
    public string? Nickname { get; init; }
    public required string GroupId { get; init; }
    public QSex Sex { get; init; }

    /// <summary>群名片。</summary>
    public string? Card { get; init; }

    /// <summary>专属头衔。</summary>
    public string? Title { get; init; }

    /// <summary>群等级。</summary>
    public int? Level { get; init; }

    public QGroupRole Role { get; init; }
    public DateTimeOffset? JoinTime { get; init; }
    public DateTimeOffset? LastSentTime { get; init; }

    /// <summary>禁言截止时间;未被禁言为 null。</summary>
    public DateTimeOffset? ShutUpEndTime { get; init; }

    public string DisplayName => !string.IsNullOrEmpty(Card) ? Card : !string.IsNullOrEmpty(Nickname) ? Nickname : UserId;
}

/// <summary>用户资料(陌生人查询)。</summary>
public sealed record QUserProfile
{
    public required string UserId { get; init; }
    public string? Nickname { get; init; }
    public string? Qid { get; init; }
    public int? Age { get; init; }
    public QSex Sex { get; init; }
    public string? Remark { get; init; }
    public string? Bio { get; init; }
    public int? Level { get; init; }
    public string? Country { get; init; }
    public string? City { get; init; }
    public string? School { get; init; }
}

/// <summary>群公告。</summary>
public sealed record QGroupAnnouncement
{
    public required string GroupId { get; init; }
    public required string AnnouncementId { get; init; }
    public string? UserId { get; init; }
    public DateTimeOffset Time { get; init; }
    public required string Content { get; init; }
    public string? ImageUrl { get; init; }
}

/// <summary>群文件。</summary>
public sealed record QGroupFile
{
    public required string GroupId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public string? ParentFolderId { get; init; } = "/";
    public long FileSize { get; init; }
    public DateTimeOffset? UploadedTime { get; init; }
    public string? UploaderId { get; init; }
    public int DownloadedTimes { get; init; }
    public DateTimeOffset? ExpireTime { get; init; }
}

/// <summary>群文件夹。</summary>
public sealed record QGroupFolder
{
    public required string GroupId { get; init; }
    public required string FolderId { get; init; }
    public string? ParentFolderId { get; init; } = "/";
    public required string FolderName { get; init; }
    public DateTimeOffset? CreatedTime { get; init; }
    public DateTimeOffset? LastModifiedTime { get; init; }
    public string? CreatorId { get; init; }
    public int FileCount { get; init; }
}

/// <summary>群精华消息。</summary>
public sealed record QEssenceMessage
{
    public required string GroupId { get; init; }
    public required string MessageId { get; init; }
    public DateTimeOffset MessageTime { get; init; }
    public string? SenderId { get; init; }
    public string? SenderName { get; init; }
    public string? OperatorId { get; init; }
    public string? OperatorName { get; init; }
    public DateTimeOffset OperationTime { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];
}

/// <summary>QQ 消息场景。</summary>
public enum QMessageScene
{
    Friend = 0,
    Group = 1,
    Temp = 2
}

/// <summary>请求/通知处理状态。</summary>
public enum QRequestState
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Ignored = 3
}

/// <summary>好友请求(列表查询)。</summary>
public sealed record QFriendRequest
{
    public DateTimeOffset Time { get; init; }
    public required string InitiatorId { get; init; }

    /// <summary>发起者 UID(用于接受/拒绝)。</summary>
    public required string InitiatorUid { get; init; }

    public string? TargetUserId { get; init; }
    public string? TargetUserUid { get; init; }
    public QRequestState State { get; init; }
    public string? Comment { get; init; }

    /// <summary>请求来源(如 群聊/搜索)。</summary>
    public string? Via { get; init; }

    public bool IsFiltered { get; init; }
}

/// <summary>群通知基类(入群申请/邀请入群/管理员变更/踢人/退群)。</summary>
public abstract record QGroupNotification
{
    public required string GroupId { get; init; }
    public required string NotificationId { get; init; }
}

/// <summary>用户入群申请通知。</summary>
public sealed record QJoinRequestNotification : QGroupNotification
{
    public required string InitiatorId { get; init; }
    public QRequestState State { get; init; }
    public string? Comment { get; init; }
    public bool IsFiltered { get; init; }
    public string? OperatorId { get; init; }
}

/// <summary>群成员邀请他人入群通知。</summary>
public sealed record QInvitedJoinRequestNotification : QGroupNotification
{
    public required string InitiatorId { get; init; }
    public required string TargetUserId { get; init; }
    public QRequestState State { get; init; }
    public string? OperatorId { get; init; }
}

/// <summary>管理员变更通知。</summary>
public sealed record QAdminChangeNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
    public bool IsSet { get; init; }
    public string? OperatorId { get; init; }
}

/// <summary>成员被踢通知。</summary>
public sealed record QKickNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
    public string? OperatorId { get; init; }
}

/// <summary>成员退群通知。</summary>
public sealed record QQuitNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
}

/// <summary>消息表情回应类型。</summary>
public enum QReactionType
{
    /// <summary>QQ 表情(face id)。</summary>
    Face = 0,

    /// <summary>Emoji 字符。</summary>
    Emoji = 1
}

/// <summary>登录账号信息。</summary>
public sealed record QLoginInfo(string Uin, string Nickname);

/// <summary>协议实现端信息。</summary>
public sealed record QImplInfo
{
    public required string ImplName { get; init; }
    public required string ImplVersion { get; init; }
    public string? QqProtocolVersion { get; init; }

    /// <summary>协议类型(windows/linux/macOS/android_pad/android_phone/ipad/iphone/harmony/watch)。</summary>
    public string? QqProtocolType { get; init; }

    /// <summary>承载协议版本(如 Milky 版本)。</summary>
    public string? ProtocolVersion { get; init; }
}
