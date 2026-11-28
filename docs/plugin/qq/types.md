# QQ C# 类型参考

命名空间：`ShiroBot.Model.QQ`。

`required` 属性必须初始化；`init` 属性在创建对象时设置；位置式 record 使用所列构造参数。
ID 与分页游标均为不透明字符串。文件大小、计数、时间及枚举保持各自类型。
继承类型同时拥有基类属性。消息、事件和申请中的 ID 应交回产生它们的适配器实例。

## 实体与枚举

### QSex

```csharp
public enum QSex
{
    Unknown = 0,
    Male = 1,
    Female = 2
}
```

### QGroupRole

```csharp
public enum QGroupRole
{
    Unknown = 0,
    Member = 1,
    Admin = 2,
    Owner = 3
}
```

### QFriendCategory

```csharp
/// <summary>好友分组。</summary>
public sealed record QFriendCategory(int CategoryId, string CategoryName);
```

### QFriend

```csharp
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
```

### QGroup

```csharp
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

    /// <summary>群分类。</summary>
    public string? ClassText { get; init; }

    /// <summary>群标签。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>跨应用群标识（QQ 官方 union_group_openid）；仅在绑定第三方应用时有效，不能代替 GroupId 调用接口。</summary>
    public string? UnionId { get; init; }

    /// <summary>入群验证问题(群主设置的问题,用于验证加入请求)。</summary>
    public string? Question { get; init; }
}
```

### QGroupMember

```csharp
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

    /// <summary>跨应用用户标识（QQ 官方 union_openid），未绑定第三方应用时为 null。</summary>
    public string? UnionId { get; init; }

    public bool IsBot { get; init; }

    /// <summary>禁言截止时间;未被禁言为 null。</summary>
    public DateTimeOffset? ShutUpEndTime { get; init; }

    public string DisplayName => !string.IsNullOrEmpty(Card) ? Card : !string.IsNullOrEmpty(Nickname) ? Nickname : UserId;
}
```

### QUserProfile

```csharp
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
    public string? AvatarUrl { get; init; }

    /// <summary>跨应用用户标识（QQ 官方 union_openid），未绑定第三方应用时为 null。</summary>
    public string? UnionId { get; init; }

    /// <summary>用户是否允许机器人主动推送消息（QQ 官方）。</summary>
    public bool? AllowProactiveMessage { get; init; }
}
```

### QGroupAnnouncement

```csharp
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
```

### QGroupFile

```csharp
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
```

### QGroupFolder

```csharp
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
```

### QFlashFile

QQ Model `1.2.0.0` 新增。

```csharp
/// <summary>闪传文件集里的一个文件。</summary>
public sealed record QFlashFile(string Name, long Size)
{
    /// <summary>本地路径；下载完成后由适配器填写，其他时候为 null。</summary>
    public string? Path { get; init; }
}
```

### QFlashShare

QQ Model `1.2.0.0` 新增。

```csharp
/// <summary>闪传分享：上传或重新分享后得到的文件集 ID、分享链接和过期时间。</summary>
public sealed record QFlashShare(string FileSetId, string ShareLink, DateTimeOffset ExpiresAt);
```

### QFlashFileSet

QQ Model `1.2.0.0` 新增。

```csharp
/// <summary>闪传文件集详情。</summary>
public sealed record QFlashFileSet
{
    public required string FileSetId { get; init; }
    public string? Title { get; init; }
    public required string ShareLink { get; init; }

    /// <summary>文件总大小（字节）。</summary>
    public long TotalSize { get; init; }

    public IReadOnlyList<QFlashFile> Files { get; init; } = [];
}
```

### QFlashTransferState

QQ Model `1.2.0.0` 新增。

```csharp
/// <summary>闪传传输阶段。</summary>
public enum QFlashTransferState
{
    Uploading = 0,
    Uploaded = 1,
    Downloading = 2,
    Downloaded = 3
}
```

### QEssenceMessage

```csharp
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
```

### QMessageScene

```csharp
/// <summary>QQ 消息场景。</summary>
public enum QMessageScene
{
    Friend = 0,
    Group = 1,
    Temp = 2
}
```

### QRequestState

```csharp
/// <summary>请求/通知处理状态。</summary>
public enum QRequestState
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Ignored = 3
}
```

### QFriendRequest

```csharp
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
```

### QGroupNotification

```csharp
/// <summary>群通知基类(入群申请/邀请入群/管理员变更/踢人/退群)。</summary>
public abstract record QGroupNotification
{
    public required string GroupId { get; init; }
    public required string NotificationId { get; init; }
}
```

### QJoinRequestNotification

```csharp
/// <summary>用户入群申请通知。</summary>
public sealed record QJoinRequestNotification : QGroupNotification
{
    public required string InitiatorId { get; init; }
    public QRequestState State { get; init; }
    public string? Comment { get; init; }
    public bool IsFiltered { get; init; }
    public string? OperatorId { get; init; }
}
```

### QInvitedJoinRequestNotification

```csharp
/// <summary>群成员邀请他人入群通知。</summary>
public sealed record QInvitedJoinRequestNotification : QGroupNotification
{
    public required string InitiatorId { get; init; }
    public required string TargetUserId { get; init; }
    public QRequestState State { get; init; }
    public string? OperatorId { get; init; }
}
```

### QAdminChangeNotification

```csharp
/// <summary>管理员变更通知。</summary>
public sealed record QAdminChangeNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
    public bool IsSet { get; init; }
    public string? OperatorId { get; init; }
}
```

### QKickNotification

```csharp
/// <summary>成员被踢通知。</summary>
public sealed record QKickNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
    public string? OperatorId { get; init; }
}
```

### QQuitNotification

```csharp
/// <summary>成员退群通知。</summary>
public sealed record QQuitNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
}
```

### QReactionType

```csharp
/// <summary>消息表情回应类型。</summary>
public enum QReactionType
{
    /// <summary>QQ 表情(face id)。</summary>
    Face = 0,

    /// <summary>Emoji 字符。</summary>
    Emoji = 1
}
```

### QLoginInfo

```csharp
/// <summary>登录账号信息。</summary>
public sealed record QLoginInfo(string Uin, string Nickname);
```

### QImplInfo

```csharp
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
```

### QOnlineClient

```csharp
/// <summary>当前账号的一个在线客户端（同一账号在其他设备上的登录）。</summary>
public sealed record QOnlineClient
{
    /// <summary>客户端 ID（协议的 app_id）。</summary>
    public required long AppId { get; init; }

    /// <summary>设备名称，例如「iPhone 15」。</summary>
    public string? DeviceName { get; init; }

    /// <summary>设备类型，例如 mobile、pc、pad。</summary>
    public string? DeviceKind { get; init; }
}
```

## 群管理与策略

### QJoinVerification

```csharp
public sealed record QJoinVerification(string? Method, string? Message, IReadOnlyList<QReviewAnswer> Answers);
```

### QReviewAnswer

```csharp
public sealed record QReviewAnswer(string? Question, string? Answer);
```

### QGroupJoinRequestPage

```csharp
public sealed record QGroupJoinRequestPage(IReadOnlyList<QGroupJoinRequest> Requests, string? NextCursor);
```

### QMemberMute

```csharp
/// <summary>将成员禁言设置为指定时长；零表示解除。不暴露协议 add/update 模式。</summary>
public sealed record QMemberMute
{
    public required string UserId { get; init; }
    public required TimeSpan Duration { get; init; }
}
```

### QOperationStatus

```csharp
public enum QOperationStatus
{
    Succeeded,
    Failed,
    Unknown,
    NotExecuted
}
```

### QOperationResult

```csharp
public sealed record QOperationResult
{
    public required string UserId { get; init; }
    public required QOperationStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}
```

### QBatchOperationResult

```csharp
/// <summary>按输入顺序返回；失败不会回滚已经完成的操作。</summary>
public sealed record QBatchOperationResult
{
    public IReadOnlyList<QOperationResult> Items { get; init; } = [];
    public bool IsSuccess => Items.All(x => x.Status == QOperationStatus.Succeeded);
}
```

### QBatchOperationCanceledException

```csharp
/// <summary>取消仍抛取消异常；PartialResult 描述已执行、结果未知和未执行的项。</summary>
public sealed class QBatchOperationCanceledException(QBatchOperationResult partialResult, CancellationToken token)
    : OperationCanceledException("Batch canceled; inspect PartialResult before retrying.", token)
{
    public QBatchOperationResult PartialResult { get; } = partialResult;
}
```

### QPatch

```csharp
/// <summary>default 为未指定；Set(value) 为设置；Clear() 为显式清空。</summary>
public readonly record struct QPatch<T>
{
    public bool IsSpecified { get; private init; }
    public bool IsClear { get; private init; }
    public T? Value { get; private init; }

    public static QPatch<T> Set(T value)
    {
        return value is null
            ? throw new ArgumentNullException(nameof(value))
            : new QPatch<T> { IsSpecified = true, Value = value };
    }

    public static QPatch<T> Clear()
    {
        return new QPatch<T> { IsSpecified = true, IsClear = true };
    }
}
```

### QMutedMember

```csharp
public sealed record QMutedMember(string UserId, DateTimeOffset? ExpiresAt, string? Username, string? UnionId);
```

### QMuteSchedule

```csharp
public sealed record QMuteSchedule(string TaskId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool Enabled);
```

### QMuteRecurring

```csharp
/// <summary>周期时段使用北京时间；结束时间早于开始时间代表跨天。</summary>
public sealed record QMuteRecurring(
    string TaskId,
    IReadOnlyList<int> Weekdays,
    string? StartTime,
    string? EndTime,
    bool Enabled);
```

### QGroupMuteState

```csharp
public sealed record QGroupMuteState(
    string? Mode,
    IReadOnlyList<QMuteSchedule> Schedules,
    IReadOnlyList<QMuteRecurring> RecurringRules,
    IReadOnlyList<QMutedMember> Members);
```

### QApprovalStrategyOptions

```csharp
public sealed record QApprovalStrategyOptions
{
    /// <summary>与 GroupNumbers 二选一，最多 100 个。</summary>
    public IReadOnlyList<string>? GroupIds { get; init; }

    public IReadOnlyList<string>? GroupNumbers { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Remark { get; init; }
}
```

### QApprovalStrategyUpdate

```csharp
public sealed record QApprovalStrategyUpdate
{
    public bool? Enabled { get; init; }
    public QPatch<DateTimeOffset> ExpiresAt { get; init; }
    public QPatch<string> Remark { get; init; }
    public QApprovalGroupAction? Groups { get; init; }
}
```

### QApprovalGroupAction

```csharp
public sealed record QApprovalGroupAction
{
    public bool Add { get; init; } = true;
    public IReadOnlyList<string>? GroupIds { get; init; }
    public IReadOnlyList<string>? GroupNumbers { get; init; }
}
```

### QApprovalStrategy

```csharp
public sealed record QApprovalStrategy
{
    public required string StrategyId { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? Remark { get; init; }
    public IReadOnlyList<string> GroupIds { get; init; } = [];

    /// <summary>平台查询响应可能脱敏，因此保留字符串。</summary>
    public IReadOnlyList<string> GroupNumbers { get; init; } = [];

    public int WhitelistUserCount { get; init; }
}
```

### QApprovalStrategyPage

```csharp
public sealed record QApprovalStrategyPage(IReadOnlyList<QApprovalStrategy> Strategies, string? NextCursor);
```

### QBlacklistedMember

```csharp
/// <summary>群黑名单中的用户；字段与 <see cref="QMutedMember" /> 一致。</summary>
public sealed record QBlacklistedMember(string UserId, DateTimeOffset? BlacklistedAt, string? Username, string? UnionId)
{
    public bool IsBot { get; init; }
}
```

### QGroupBlacklistPage

```csharp
public sealed record QGroupBlacklistPage(IReadOnlyList<QBlacklistedMember> Members, string? NextCursor);
```

### QGroupReceiveSetting

```csharp
/// <summary>群内消息接收设置（QQ 官方 receive_setting）。</summary>
public enum QGroupReceiveSetting
{
    Unknown = 0,

    /// <summary>接收全部消息（all）。</summary>
    All = 1,

    /// <summary>仅接收 @机器人 的消息（only_mention）。</summary>
    MentionOnly = 2,

    /// <summary>接收 @机器人 的消息及其上下文（mention_and_context）。</summary>
    MentionAndContext = 3
}
```

### QGroupBotState

```csharp
/// <summary>机器人在群内的状态。成员身份（群内 ID、角色、入群时间）沿用 <see cref="QGroupMember" />。</summary>
public sealed record QGroupBotState
{
    public required QGroupMember Member { get; init; }
    public bool AllowProactiveMessage { get; init; }
    public QGroupReceiveSetting ReceiveSetting { get; init; }
}
```

## 原生消息

### QIncomingMessage

```csharp
/// <summary>QQ 入站消息(协议无关)。</summary>
public abstract record QIncomingMessage
{
    public required string PeerId { get; init; }
    public required string MessageId { get; init; }
    public required string SenderId { get; init; }
    public DateTimeOffset Time { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];

    public abstract QMessageScene Scene { get; }

    public string GetPlainText()
    {
        return string.Concat(Segments.OfType<QIncomingText>().Select(segment => segment.Text));
    }
}
```

### QFriendMessage

```csharp
/// <summary>好友消息。</summary>
public sealed record QFriendMessage : QIncomingMessage
{
    public required QFriend Friend { get; init; }
    public override QMessageScene Scene => QMessageScene.Friend;
}
```

### QGroupMessage

```csharp
/// <summary>群消息。</summary>
public sealed record QGroupMessage : QIncomingMessage
{
    public required QGroup Group { get; init; }
    public required QGroupMember GroupMember { get; init; }
    public override QMessageScene Scene => QMessageScene.Group;
}
```

### QTempMessage

```csharp
/// <summary>临时会话消息。</summary>
public sealed record QTempMessage : QIncomingMessage
{
    public QGroup? Group { get; init; }
    public override QMessageScene Scene => QMessageScene.Temp;
}
```

### QForwardedIncomingMessage

```csharp
/// <summary>合并转发内的一条消息。</summary>
public sealed record QForwardedIncomingMessage
{
    public string? MessageId { get; init; }
    public string? SenderName { get; init; }
    public string? AvatarUrl { get; init; }
    public DateTimeOffset Time { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];
}
```

### QSentMessage

```csharp
/// <summary>QQ 原生消息发送结果。</summary>
public sealed record QSentMessage(string MessageId, DateTimeOffset Time);
```

## 入站与出站消息段

### QIncomingSegment

```csharp
/// <summary>QQ 入站消息段基类(协议无关,由具体适配器从 Milky/OneBot 等映射)。</summary>
public abstract record QIncomingSegment;
```

### QIncomingText

对应主消息段：[`TextSegment`](../segments#textsegment)

```csharp
public sealed record QIncomingText(string Text) : QIncomingSegment;
```

### QIncomingMention

对应主消息段：[`MentionSegment`](../segments#mentionsegment)

```csharp
public sealed record QIncomingMention(string UserId, string Name) : QIncomingSegment;
```

### QIncomingMentionAll

对应主消息段：[`MentionAllSegment`](../segments#mentionallsegment)

```csharp
public sealed record QIncomingMentionAll : QIncomingSegment;
```

### QIncomingFace

对应主消息段：[`EmojiSegment`](../segments#emojisegment)

```csharp
/// <summary>QQ 表情。</summary>
public sealed record QIncomingFace(string FaceId, bool IsLarge = false) : QIncomingSegment;
```

### QIncomingReply

对应主消息段：[`QuoteSegment`](../segments#quotesegment)

```csharp
public sealed record QIncomingReply(string MessageId) : QIncomingSegment
{
    public string? SenderId { get; init; }
    public string? SenderName { get; init; }
    public DateTimeOffset? Time { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];
}
```

### QIncomingImage

对应主消息段：[`ImageSegment`](../segments#imagesegment)

```csharp
public sealed record QIncomingImage(string ResourceId, string TempUrl) : QIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Summary { get; init; }

    /// <summary>是否闪照/表情图等子类型(协议自定义字符串)。</summary>
    public string? SubType { get; init; }
}
```

### QIncomingRecord

对应主消息段：[`AudioSegment`](../segments#audiosegment)

```csharp
public sealed record QIncomingRecord(string ResourceId, string TempUrl) : QIncomingSegment
{
    public TimeSpan Duration { get; init; }
}
```

### QIncomingVideo

对应主消息段：[`VideoSegment`](../segments#videosegment)

```csharp
public sealed record QIncomingVideo(string ResourceId, string TempUrl) : QIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### QIncomingBubbleVideo

气泡视频，QQ Model `1.2.0.0` 新增。字段与 `QIncomingVideo` 相同，只是消息段类型不同。

```csharp
/// <summary>气泡视频：字段与 <see cref="QIncomingVideo" /> 相同，只是消息段类型不同。</summary>
public sealed record QIncomingBubbleVideo(string ResourceId, string TempUrl) : QIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### QIncomingFile

对应主消息段：[`FileSegment`](../segments#filesegment)

```csharp
public sealed record QIncomingFile(string FileId, string FileName, long FileSize) : QIncomingSegment
{
    public string? FileHash { get; init; }
}
```

### QIncomingFlashFile

闪传文件，QQ Model `1.2.0.0` 新增。

```csharp
/// <summary>闪传文件（收到的闪传分享）。用 <see cref="IQFileApi.GetFlashFileSetAsync" /> 查看详情。</summary>
public sealed record QIncomingFlashFile(string FileSetId, string Title) : QIncomingSegment
{
    /// <summary>协议原样提供的场景类型，含义未公开。</summary>
    public int SceneType { get; init; }
}
```

### QIncomingForward

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
/// <summary>合并转发。</summary>
public sealed record QIncomingForward(string ForwardId) : QIncomingSegment
{
    public string? Title { get; init; }
    public IReadOnlyList<string> Preview { get; init; } = [];
    public string? Summary { get; init; }
}
```

### QIncomingMarketFace

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
/// <summary>商城表情。</summary>
public sealed record QIncomingMarketFace(string EmojiId, string Url) : QIncomingSegment
{
    public int EmojiPackageId { get; init; }
    public string? Key { get; init; }
    public string? Summary { get; init; }
}
```

### QIncomingLightApp

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
/// <summary>小程序 / 卡片消息。</summary>
public sealed record QIncomingLightApp(string AppName, string JsonPayload) : QIncomingSegment;
```

### QIncomingXml

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
public sealed record QIncomingXml(int ServiceId, string XmlPayload) : QIncomingSegment;
```

### QIncomingMarkdown

对应主消息段：[`MarkdownSegment`](../segments#markdownsegment)

```csharp
public sealed record QIncomingMarkdown(string Content) : QIncomingSegment;
```

### QOutgoingSegment

```csharp
/// <summary>QQ 出站消息段基类。</summary>
public abstract record QOutgoingSegment;
```

### QOutgoingText

对应主消息段：[`TextSegment`](../segments#textsegment)

```csharp
public sealed record QOutgoingText(string Text) : QOutgoingSegment;
```

### QOutgoingMention

对应主消息段：[`MentionSegment`](../segments#mentionsegment)

```csharp
public sealed record QOutgoingMention(string UserId) : QOutgoingSegment;
```

### QOutgoingMentionAll

对应主消息段：[`MentionAllSegment`](../segments#mentionallsegment)

```csharp
public sealed record QOutgoingMentionAll : QOutgoingSegment;
```

### QOutgoingFace

对应主消息段：[`EmojiSegment`](../segments#emojisegment)

```csharp
public sealed record QOutgoingFace(string FaceId, bool IsLarge = false) : QOutgoingSegment;
```

### QOutgoingReply

对应主消息段：[`QuoteSegment`](../segments#quotesegment)

```csharp
public sealed record QOutgoingReply(string MessageId) : QOutgoingSegment;
```

### QOutgoingImage

对应主消息段：[`ImageSegment`](../segments#imagesegment)

```csharp
/// <summary>Uri 支持 http(s)://、file://、base64://。</summary>
public sealed record QOutgoingImage(string Uri) : QOutgoingSegment
{
    public string? Summary { get; init; }

    /// <summary>协议自定义子类型(如 normal / sticker)。</summary>
    public string? SubType { get; init; }
}
```

### QOutgoingRecord

对应主消息段：[`AudioSegment`](../segments#audiosegment)

```csharp
public sealed record QOutgoingRecord(string Uri) : QOutgoingSegment;
```

### QOutgoingVideo

对应主消息段：[`VideoSegment`](../segments#videosegment)

```csharp
public sealed record QOutgoingVideo(string Uri) : QOutgoingSegment
{
    public string? ThumbUri { get; init; }
}
```

### QOutgoingBubbleVideo

气泡视频，QQ Model `1.2.0.0` 新增。字段与 `QOutgoingVideo` 相同，只是消息段类型不同；适配器不支持时应拒绝发送，而不是当作普通视频发出。

```csharp
/// <summary>气泡视频：字段与 <see cref="QOutgoingVideo" /> 相同，只是消息段类型不同。Uri 支持 http(s)://、file://、base64://。</summary>
public sealed record QOutgoingBubbleVideo(string Uri) : QOutgoingSegment
{
    public string? ThumbUri { get; init; }
}
```

### QOutgoingLightApp

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
/// <summary>小程序 / 卡片消息(出站)。</summary>
public sealed record QOutgoingLightApp(string JsonPayload) : QOutgoingSegment;
```

### QForwardedMessage

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
/// <summary>合并转发的一条消息。</summary>
public sealed record QForwardedMessage(string UserId, string SenderName, IReadOnlyList<QOutgoingSegment> Segments)
{
    /// <summary>消息展示时间,null 使用当前时间。</summary>
    public DateTimeOffset? Time { get; init; }
}
```

### QOutgoingForward

对应主消息段：[`RawSegment`](../segments#rawsegment)

```csharp
/// <summary>合并转发。</summary>
public sealed record QOutgoingForward(IReadOnlyList<QForwardedMessage> Messages) : QOutgoingSegment
{
    public string? Title { get; init; }
    public IReadOnlyList<string>? Preview { get; init; }
    public string? Summary { get; init; }
    public string? Prompt { get; init; }
}
```

## 事件与事件 Kind

### QEventPayload

```csharp
/// <summary>
/// QQ 平台特有事件负载(协议无关)。适配器把它们放进 PlatformEvent.Raw,
/// Kind 使用 <see cref="QEventKinds"/> 中的常量;插件按 Kind 订阅后把 Raw 转成对应类型。
/// </summary>
public abstract record QEventPayload
{
    public DateTimeOffset Time { get; init; }
    public string? SelfId { get; init; }
    /// <summary>平台提供的事件 ID；不是所有事件都可用它被动回复。</summary>
    public string? EventId { get; init; }
}
```

### QEventKinds

```csharp
/// <summary>PlatformEvent.Kind 常量表。</summary>
public static class QEventKinds
{
    /// <summary>QQ 官方 INTERACTION_CREATE，type=11 的消息按钮点击。</summary>
    public const string OfficialButtonInteraction = "official_button_interaction";
    /// <summary>机器人被拉入群（GROUP_ADD_ROBOT）。适配器上报为 GuildInviteEvent，Raw 为 <see cref="QOfficialLifecycleEvent"/>。</summary>
    public const string OfficialGroupAddRobot = "official_group_add_robot";
    /// <summary>机器人被移出群（GROUP_DEL_ROBOT）。适配器上报为 MemberLeftEvent，Raw 为 <see cref="QOfficialLifecycleEvent"/>。</summary>
    public const string OfficialGroupDelRobot = "official_group_del_robot";
    /// <summary>群里关闭了主动消息（GROUP_MSG_REJECT）。</summary>
    public const string OfficialGroupMsgReject = "official_group_msg_reject";
    /// <summary>群里开启了主动消息（GROUP_MSG_RECEIVE）。</summary>
    public const string OfficialGroupMsgReceive = "official_group_msg_receive";
    /// <summary>用户添加机器人为好友（FRIEND_ADD）。</summary>
    public const string OfficialFriendAdd = "official_friend_add";
    /// <summary>用户删除机器人好友（FRIEND_DEL）。</summary>
    public const string OfficialFriendDel = "official_friend_del";
    /// <summary>用户关闭了单聊主动消息（C2C_MSG_REJECT）。</summary>
    public const string OfficialC2CMsgReject = "official_c2c_msg_reject";
    /// <summary>用户开启了单聊主动消息（C2C_MSG_RECEIVE）。</summary>
    public const string OfficialC2CMsgReceive = "official_c2c_msg_receive";
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
    public const string GroupDisband = "group_disband";
    public const string PeerPinChange = "peer_pin_change";
    /// <summary>闪传上传或下载的进度与完成。Raw 为 <see cref="QFlashFileTransfer"/>。</summary>
    public const string FlashFileTransfer = "flash_file_transfer";
}
```

### QOfficialButtonInteraction

```csharp
/// <summary>
/// QQ 官方消息按钮回调。适配器将 INTERACTION_CREATE(type=11) 映射到
/// PlatformEvent.Raw，并使用 QEventKinds.OfficialButtonInteraction 作为 Kind。
/// </summary>
public sealed record QOfficialButtonInteraction : QEventPayload
{
    /// <summary>互动 ID，用于调用 PUT /interactions/{interaction_id}。</summary>
    public required string InteractionId { get; init; }
    /// <summary>按钮 action.data，即事件 data.resolved.button_data。</summary>
    public required string ButtonData { get; init; }
    /// <summary>按钮 ID；发送时未指定则可能为空。</summary>
    public string? ButtonId { get; init; }
    /// <summary>被点击按钮所在的消息 ID，平台未提供时为空。</summary>
    public string? MessageId { get; init; }
    /// <summary>发生点击的目标会话，频道场景可使用 channel_id。</summary>
    public required QOfficialMessageTarget Target { get; init; }
    /// <summary>操作者 ID：单聊 user_openid、群聊 group_member_openid、频道 user_id。</summary>
    public required string UserId { get; init; }
    /// <summary>频道场景的 guild_id。</summary>
    public string? GuildId { get; init; }
}
```

### QOfficialLifecycleEvent

```csharp
/// <summary>
/// QQ 官方机器人在群或单聊里的生命周期事件（加群、被移出、好友变化、主动消息开关）。
/// Kind 见 <see cref="QEventKinds"/> 中的 Official* 常量。
/// </summary>
public sealed record QOfficialLifecycleEvent : QEventPayload
{
    /// <summary>事件发生的会话。</summary>
    public required QOfficialMessageTarget Target { get; init; }
    /// <summary>操作者的 openid：群事件为 op_member_openid，单聊事件为用户 openid。</summary>
    public string? OperatorId { get; init; }
    /// <summary>平台给出的事件时间。</summary>
    public DateTimeOffset? EventTime { get; init; }
}
```

### QFriendNudge

```csharp
/// <summary>好友戳一戳。</summary>
public sealed record QFriendNudge : QEventPayload
{
    public required string UserId { get; init; }
    public bool IsSelfSend { get; init; }
    public bool IsSelfReceive { get; init; }
    public string? DisplayAction { get; init; }
    public string? DisplaySuffix { get; init; }

    /// <summary>戳一戳动作图片 URL(存在时用于取代动作提示文本)。</summary>
    public string? DisplayActionImgUrl { get; init; }
}
```

### QFlashFileTransfer

QQ Model `1.2.0.0` 新增，Kind 为 `QEventKinds.FlashFileTransfer`。

```csharp
/// <summary>闪传上传或下载的进度与完成。</summary>
public sealed record QFlashFileTransfer : QEventPayload
{
    public required QFlashTransferState State { get; init; }
    public required string FileSetId { get; init; }
    public string? Title { get; init; }
    public string? ShareLink { get; init; }

    /// <summary>文件列表；下载完成时带本地路径。</summary>
    public IReadOnlyList<QFlashFile> Files { get; init; } = [];

    /// <summary>已上传或已下载的字节数，传输中才有。</summary>
    public long? TransferredSize { get; init; }

    /// <summary>总字节数。</summary>
    public long? TotalSize { get; init; }

    /// <summary>当前速度（字节/秒）。</summary>
    public long? Speed { get; init; }

    /// <summary>预计剩余时间。</summary>
    public TimeSpan? RemainingTime { get; init; }
}
```

### QFriendFileUpload

```csharp
/// <summary>好友文件上传。</summary>
public sealed record QFriendFileUpload : QEventPayload
{
    public required string UserId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public string? FileHash { get; init; }
    public bool IsSelf { get; init; }
}
```

### QGroupAdminChange

```csharp
/// <summary>群管理员变更。</summary>
public sealed record QGroupAdminChange : QEventPayload
{
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public string? OperatorId { get; init; }
    public bool IsSet { get; init; }
}
```

### QGroupEssenceMessageChange

```csharp
/// <summary>群精华消息变更。</summary>
public sealed record QGroupEssenceMessageChange : QEventPayload
{
    public required string GroupId { get; init; }
    public required string MessageId { get; init; }
    public string? OperatorId { get; init; }
    public bool IsSet { get; init; }
}
```

### QGroupNameChange

```csharp
/// <summary>群名变更。</summary>
public sealed record QGroupNameChange : QEventPayload
{
    public required string GroupId { get; init; }
    public required string NewGroupName { get; init; }
    public string? OperatorId { get; init; }
}
```

### QGroupMessageReaction

```csharp
/// <summary>群消息表情回应。</summary>
public sealed record QGroupMessageReaction : QEventPayload
{
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public required string MessageId { get; init; }
    public required string FaceId { get; init; }

    /// <summary>回应类型(QQ 表情 / Emoji 字符)。</summary>
    public QReactionType ReactionType { get; init; }

    public bool IsAdd { get; init; }
}
```

### QGroupMute

```csharp
/// <summary>群禁言(Duration 为零表示解除)。</summary>
public sealed record QGroupMute : QEventPayload
{
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public string? OperatorId { get; init; }
    public TimeSpan Duration { get; init; }
    public bool IsUnmute => Duration == TimeSpan.Zero;
}
```

### QGroupWholeMute

```csharp
/// <summary>全员禁言。</summary>
public sealed record QGroupWholeMute : QEventPayload
{
    public required string GroupId { get; init; }
    public string? OperatorId { get; init; }
    public bool IsMute { get; init; }
}
```

### QGroupNudge

```csharp
/// <summary>群戳一戳。</summary>
public sealed record QGroupNudge : QEventPayload
{
    public required string GroupId { get; init; }
    public required string SenderId { get; init; }
    public required string ReceiverId { get; init; }
    public string? DisplayAction { get; init; }
    public string? DisplaySuffix { get; init; }

    /// <summary>戳一戳动作图片 URL(存在时用于取代动作提示文本)。</summary>
    public string? DisplayActionImgUrl { get; init; }
}
```

### QGroupFileUpload

```csharp
/// <summary>群文件上传。</summary>
public sealed record QGroupFileUpload : QEventPayload
{
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
}
```

### QGroupJoinRequest

```csharp
/// <summary>统一入群申请。RequestId 是不透明审批凭据，应原样传回 IQGroupApi。</summary>
public sealed record QGroupJoinRequest : QEventPayload
{
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public required string RequestId { get; init; }
    public string? Username { get; init; }
    public string? Comment { get; init; }
    public bool IsInvited { get; init; }
    public bool IsFiltered { get; init; }
    public string? InviterId { get; init; }
    public QRequestState State { get; init; }
    public string? RiskTips { get; init; }
    public string? UnionId { get; init; }
    public bool IsBot { get; init; }
    public QJoinVerification? Verification { get; init; }
    public string? AutoApprovedStrategyId { get; init; }
}
```

### QGroupDisband

```csharp
/// <summary>群解散。</summary>
public sealed record QGroupDisband : QEventPayload
{
    public required string GroupId { get; init; }
    public string? OperatorId { get; init; }
}
```

### QPeerPinChange

```csharp
/// <summary>会话置顶变更。</summary>
public sealed record QPeerPinChange : QEventPayload
{
    public required QMessageScene Scene { get; init; }
    public required string PeerId { get; init; }
    public bool IsPinned { get; init; }
}
```

### QMessageRecall

```csharp
/// <summary>消息撤回的 QQ 原始信息。</summary>
public sealed record QMessageRecall : QEventPayload
{
    public required QMessageScene Scene { get; init; }
    public required string PeerId { get; init; }
    public required string MessageId { get; init; }
    public required string SenderId { get; init; }
    public required string OperatorId { get; init; }
    public string? DisplaySuffix { get; init; }
}
```

### QGroupMemberIncrease

```csharp
/// <summary>群成员增加的 QQ 原始信息。</summary>
public sealed record QGroupMemberIncrease : QEventPayload
{
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public string? OperatorId { get; init; }
    public string? InvitorId { get; init; }
    /// <summary>官方平台跨场景的 user_openid（存在时）。</summary>
    public string? GlobalUserId { get; init; }
}
```

### QGroupMemberDecrease

```csharp
/// <summary>群成员减少的 QQ 原始信息。</summary>
public sealed record QGroupMemberDecrease : QEventPayload
{
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public string? OperatorId { get; init; }
    /// <summary>官方平台跨场景的 user_openid（存在时）。</summary>
    public string? GlobalUserId { get; init; }
}
```

### QFriendRequestReceived

```csharp
/// <summary>好友请求的 QQ 原始信息。</summary>
public sealed record QFriendRequestReceived : QEventPayload
{
    public required string InitiatorId { get; init; }
    public required string InitiatorUid { get; init; }
    public string? Comment { get; init; }
    public string? Via { get; init; }
}
```

### QGroupInvitation

```csharp
/// <summary>机器人被邀请入群的 QQ 原始信息。</summary>
public sealed record QGroupInvitation : QEventPayload
{
    public required string GroupId { get; init; }
    public required string InvitationId { get; init; }
    public required string InitiatorId { get; init; }
    public string? SourceGroupId { get; init; }
}
```

### QBotOffline

```csharp
/// <summary>机器人离线的 QQ 原始信息。</summary>
public sealed record QBotOffline : QEventPayload
{
    public string? Reason { get; init; }
}
```

## 官方消息、Markdown、按钮与媒体

### QOfficialMessageTarget

```csharp
/// <summary>QQ 官方机器人消息目标。单聊/群聊 Id 是开放平台 openid；频道 Id 是 channel_id；频道私信 Id 是 guild_id。</summary>
public sealed record QOfficialMessageTarget(QOfficialMessageScene Scene, string Id);
```

### QOfficialMessage

```csharp
/// <summary>一条 QQ 官方消息。根据消息种类限制可组合的正文和键盘。</summary>
public abstract record QOfficialMessage
{
    public static QOfficialMessage Text(string content)
    {
        return new QOfficialTextMessage(content);
    }

    public static QOfficialMessage Markdown(QOfficialMarkdown markdown, QOfficialKeyboard? keyboard = null)
    {
        return new QOfficialMarkdownMessage(markdown, keyboard);
    }

    /// <summary>创建本地媒体消息；媒体上传由适配器在发送时完成。</summary>
    public static QOfficialMessage Media(QOfficialMediaType type, Stream content, string fileName,
        string? caption = null)
    {
        return new QOfficialMediaSourceMessage(type, content, fileName, caption);
    }

    /// <summary>创建已上传媒体消息，可在被动回复时复用 file_info。</summary>
    public static QOfficialMessage Media(QOfficialMedia media, string? caption = null)
    {
        return new QOfficialUploadedMediaMessage(media, caption);
    }
}
```

### QOfficialTextMessage

```csharp
public sealed record QOfficialTextMessage(string Content) : QOfficialMessage;
```

### QOfficialMarkdownMessage

```csharp
public sealed record QOfficialMarkdownMessage(QOfficialMarkdown Content, QOfficialKeyboard? Keyboard = null)
    : QOfficialMessage;
```

### QOfficialMediaSourceMessage

```csharp
public sealed record QOfficialMediaSourceMessage(
    QOfficialMediaType Type,
    Stream Content,
    string FileName,
    string? Caption = null) : QOfficialMessage;
```

### QOfficialUploadedMediaMessage

```csharp
public sealed record QOfficialUploadedMediaMessage(QOfficialMedia UploadedMedia, string? Caption = null)
    : QOfficialMessage;
```

### QOfficialMessageScene

```csharp
public enum QOfficialMessageScene
{
    Direct = 0,
    Group = 1,
    Channel = 2,
    ChannelDirect = 3
}
```

### QOfficialMarkdown

对应主消息段：[`MarkdownSegment`](../segments#markdownsegment)

```csharp
/// <summary>QQ 官方 Markdown 消息。发送时在自定义内容和模板之间选择一种。</summary>
public abstract record QOfficialMarkdown
{
    /// <summary>图片转存失败时拒绝发送整条消息。</summary>
    public bool? ForceVerifyImageResource { get; init; }
}
```

### QCustomMarkdown

对应主消息段：[`MarkdownSegment`](../segments#markdownsegment)

```csharp
public sealed record QCustomMarkdown(string Content) : QOfficialMarkdown;
```

### QTemplateMarkdown

```csharp
public sealed record QTemplateMarkdown(
    string CustomTemplateId,
    IReadOnlyList<QMarkdownParameter> Params) : QOfficialMarkdown;
```

### QMarkdownParameter

```csharp
public sealed record QMarkdownParameter(string Key, IReadOnlyList<string> Values);
```

### QOfficialEmbed

```csharp
/// <summary>QQ 官方 Embed 卡片。字段格式按官方 Embed 消息模型定义。</summary>
public sealed record QOfficialEmbed
{
    public string? Title { get; init; }
    public string? Prompt { get; init; }
    public QOfficialEmbedThumbnail? Thumbnail { get; init; }
    public IReadOnlyList<QOfficialEmbedField>? Fields { get; init; }
}
```

### QOfficialEmbedThumbnail

```csharp
public sealed record QOfficialEmbedThumbnail(string Url);
```

### QOfficialEmbedField

```csharp
public sealed record QOfficialEmbedField(string Name);
```

### QOfficialKeyboard

对应主消息段：[`MessageButton`](../segments#messagebutton)

```csharp
/// <summary>QQ 官方按钮键盘。模板 ID 和自定义按钮内容互斥。</summary>
public abstract record QOfficialKeyboard;
```

### QKeyboardTemplate

```csharp
public sealed record QKeyboardTemplate(string Id) : QOfficialKeyboard;
```

### QInlineKeyboard

对应主消息段：[`MessageButton`](../segments#messagebutton)

```csharp
/// <summary>自定义按钮最多五行，每行最多五个按钮；需要开放平台开通相应能力。</summary>
public sealed record QInlineKeyboard(IReadOnlyList<QKeyboardRow> Rows) : QOfficialKeyboard;
```

### QKeyboardRow

```csharp
public sealed record QKeyboardRow(IReadOnlyList<QKeyboardButton> Buttons);
```

### QKeyboardButton

```csharp
public sealed record QKeyboardButton
{
    /// <summary>在同一键盘内唯一。</summary>
    public string? Id { get; init; }

    /// <summary>仅回调按钮有效；组内一个按钮操作后，其余按钮变灰。</summary>
    public string? GroupId { get; init; }

    public required QKeyboardRenderData RenderData { get; init; }
    public required QKeyboardAction Action { get; init; }
}
```

### QKeyboardRenderData

```csharp
public sealed record QKeyboardRenderData(string Label, string VisitedLabel, QKeyboardButtonStyle Style);
```

### QKeyboardButtonStyle

```csharp
public enum QKeyboardButtonStyle
{
    Gray = 0,
    Blue = 1,
    Red = 3,
    BlueFilled = 4
}
```

### QKeyboardAction

```csharp
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

    public QKeyboardModal? Modal { get; init; }
}
```

### QKeyboardModal

```csharp
/// <summary>按钮点击二次确认。Content 最多 40 字符且不能包含 URL，按钮文字最多 4 字符。</summary>
public sealed record QKeyboardModal(string Content, string? ConfirmText = null, string? CancelText = null);
```

### QKeyboardActionType

```csharp
public enum QKeyboardActionType
{
    Jump = 0,
    Callback = 1,
    Command = 2
}
```

### QKeyboardPermission

```csharp
public sealed record QKeyboardPermission
{
    public required QKeyboardPermissionType Type { get; init; }
    public IReadOnlyList<string>? SpecifyUserIds { get; init; }

    /// <summary>仅频道的指定身份组权限有效。</summary>
    public IReadOnlyList<string>? SpecifyRoleIds { get; init; }
}
```

### QKeyboardPermissionType

```csharp
public enum QKeyboardPermissionType
{
    SpecifiedUsers = 0,
    Managers = 1,
    Everyone = 2,
    SpecifiedRoles = 3
}
```

### QOfficialMessageReply

```csharp
/// <summary>被动回复信息。主动发送时留空。</summary>
public sealed record QOfficialMessageReply
{
    public string? MessageId { get; init; }
    public string? EventId { get; init; }
    public int? MessageSequence { get; init; }

    /// <summary>引用消息的 QQ REFIDX_ 值；用于让客户端显示引用关系。</summary>
    public string? ReferenceIndex { get; init; }
}
```

### QOfficialMediaType

```csharp
/// <summary>QQ 官方群媒体类型。</summary>
public enum QOfficialMediaType
{
    Image = 1,
    Video = 2,
    Audio = 3,
    File = 4
}
```

### QOfficialMedia

```csharp
/// <summary>已上传到 QQ 官方平台的媒体引用，可用于后续发送。</summary>
public sealed record QOfficialMedia(string FileInfo, QOfficialMediaType Type, string FileName);
```

### QOfficialStreamContentType

```csharp
public enum QOfficialStreamContentType
{
    Text = 0,
    Markdown = 1
}
```

### QOfficialInteractionResponseCode

```csharp
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
```

## 官方图片素材

### QOfficialImageMaterial

```csharp
/// <summary>Url 为 qqbot-media:// 引用；PreviewUrl 是短效地址，不应持久化。</summary>
public sealed record QOfficialImageMaterial(string ImageId, string Url)
{
    public string? FileName { get; init; }
    public string? PreviewUrl { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
```

### QOfficialImageMaterialPage

```csharp
public sealed record QOfficialImageMaterialPage(
    IReadOnlyList<QOfficialImageMaterial> Images, string? NextCursor, int Total);
```

## 异常

### QPlatformException

```csharp
/// <summary>QQ 平台拒绝了某个操作。适配器负责识别错误码，插件只按类型处理结果。</summary>
public abstract class QPlatformException(string message, string operation, int? errorCode, string? detail)
    : Exception(message)
{
    /// <summary>被拒绝的操作，例如“群黑名单查询”。</summary>
    public string Operation { get; } = operation;

    public int? ErrorCode { get; } = errorCode;

    public string? Detail { get; } = detail;

    private protected static string Code(int? errorCode) => errorCode?.ToString() ?? "unknown";
}
```

### QPermissionDeniedException

```csharp
/// <summary>平台未给当前机器人开通某个 QQ 接口权限。</summary>
public sealed class QPermissionDeniedException(string operation, int? errorCode, string? detail = null)
    : QPlatformException($"QQ 平台未授权接口：{operation}（错误码 {Code(errorCode)}）", operation, errorCode, detail);
```

### QLimitExceededException

```csharp
/// <summary>QQ 平台限制了操作次数，例如每群每天一次的 @全体成员。</summary>
public sealed class QLimitExceededException(string operation, int? errorCode, string? detail = null)
    : QPlatformException($"QQ 平台操作次数已用完：{operation}（错误码 {Code(errorCode)}）", operation, errorCode, detail);
```

## 群管理能力标记

```csharp
[Flags]
public enum QGroupCapabilities
{
    None = 0,
    Rename = 1 << 0,
    Avatar = 1 << 1,
    MemberCard = 1 << 2,
    MemberTitle = 1 << 3,
    MemberAdmin = 1 << 4,
    MemberMute = 1 << 5,
    WholeMute = 1 << 6,
    Kick = 1 << 7,
    Quit = 1 << 8,
    Nudge = 1 << 9,
    Reaction = 1 << 10,
    Announcement = 1 << 11,
    Essence = 1 << 12,
    JoinRequests = 1 << 13,
    Notifications = 1 << 14,
    Invitations = 1 << 15,
    MuteState = 1 << 16,
    BatchMute = 1 << 17,
    RejectAndBlacklist = 1 << 18,
    GroupList = 1 << 19,
    GroupInfo = 1 << 20,
    Members = 1 << 21,
    MemberBlacklist = 1 << 22,
    BotState = 1 << 23
}
```
