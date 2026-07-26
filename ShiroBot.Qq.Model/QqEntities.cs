namespace ShiroBot.Qq.Model;

public enum QqSex
{
    Unknown,
    Male,
    Female
}

public enum QqGroupRole
{
    Member,
    Admin,
    Owner
}

/// <summary>好友分组。</summary>
public sealed record QqFriendCategory(int CategoryId, string CategoryName);

/// <summary>好友。</summary>
public sealed record QqFriend
{
    public required long UserId { get; init; }
    public required string Nickname { get; init; }
    public QqSex Sex { get; init; }
    public string? Qid { get; init; }
    public string? Remark { get; init; }
    public QqFriendCategory? Category { get; init; }
}

/// <summary>群。</summary>
public sealed record QqGroup
{
    public required long GroupId { get; init; }
    public required string GroupName { get; init; }
    public int MemberCount { get; init; }
    public int MaxMemberCount { get; init; }
    public string? Remark { get; init; }
    public DateTimeOffset? CreatedTime { get; init; }
    public string? Description { get; init; }
    public string? Announcement { get; init; }
}

/// <summary>群成员。</summary>
public sealed record QqGroupMember
{
    public required long UserId { get; init; }
    public required string Nickname { get; init; }
    public required long GroupId { get; init; }
    public QqSex Sex { get; init; }

    /// <summary>群名片。</summary>
    public string? Card { get; init; }

    /// <summary>专属头衔。</summary>
    public string? Title { get; init; }

    /// <summary>群等级。</summary>
    public int Level { get; init; }

    public QqGroupRole Role { get; init; }
    public DateTimeOffset? JoinTime { get; init; }
    public DateTimeOffset? LastSentTime { get; init; }

    /// <summary>禁言截止时间;未被禁言为 null。</summary>
    public DateTimeOffset? ShutUpEndTime { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Card) ? Nickname : Card;
}

/// <summary>用户资料(陌生人查询)。</summary>
public sealed record QqUserProfile
{
    public required long UserId { get; init; }
    public required string Nickname { get; init; }
    public string? Qid { get; init; }
    public int Age { get; init; }
    public QqSex Sex { get; init; }
    public string? Remark { get; init; }
    public string? Bio { get; init; }
    public int Level { get; init; }
    public string? Country { get; init; }
    public string? City { get; init; }
    public string? School { get; init; }
}

/// <summary>群公告。</summary>
public sealed record QqGroupAnnouncement
{
    public required long GroupId { get; init; }
    public required string AnnouncementId { get; init; }
    public long UserId { get; init; }
    public DateTimeOffset Time { get; init; }
    public required string Content { get; init; }
    public string? ImageUrl { get; init; }
}

/// <summary>群文件。</summary>
public sealed record QqGroupFile
{
    public required long GroupId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public string ParentFolderId { get; init; } = "/";
    public long FileSize { get; init; }
    public DateTimeOffset? UploadedTime { get; init; }
    public long UploaderId { get; init; }
    public int DownloadedTimes { get; init; }
    public DateTimeOffset? ExpireTime { get; init; }
}

/// <summary>群文件夹。</summary>
public sealed record QqGroupFolder
{
    public required long GroupId { get; init; }
    public required string FolderId { get; init; }
    public string ParentFolderId { get; init; } = "/";
    public required string FolderName { get; init; }
    public DateTimeOffset? CreatedTime { get; init; }
    public long CreatorId { get; init; }
    public int FileCount { get; init; }
}

/// <summary>群精华消息。</summary>
public sealed record QqEssenceMessage
{
    public required long GroupId { get; init; }
    public required long MessageSeq { get; init; }
    public DateTimeOffset MessageTime { get; init; }
    public long SenderId { get; init; }
    public string? SenderName { get; init; }
    public long OperatorId { get; init; }
    public string? OperatorName { get; init; }
    public DateTimeOffset OperationTime { get; init; }
    public IReadOnlyList<QqIncomingSegment> Segments { get; init; } = [];
}

/// <summary>QQ 消息场景。</summary>
public enum QqMessageScene
{
    Friend,
    Group,
    Temp
}
