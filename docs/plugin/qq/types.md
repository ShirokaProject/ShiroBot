# QQ C# 类型参考

命名空间：`ShiroBot.Model.QQ`。以下完整声明对应 QQ Model ABI `1.0.0.0`。

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
public sealed record QFriendCategory(int CategoryId, string CategoryName);
```

### QFriend

```csharp
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
```

### QGroupMember

```csharp
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
```

### QUserProfile

```csharp
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
```

### QGroupAnnouncement

```csharp
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

### QEssenceMessage

```csharp
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
public enum QMessageScene
{
    Friend = 0,
    Group = 1,
    Temp = 2
}
```

### QRequestState

```csharp
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
public abstract record QGroupNotification
{
    public required string GroupId { get; init; }
    public required string NotificationId { get; init; }
}
```

### QJoinRequestNotification

```csharp
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
public sealed record QAdminChangeNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
    public bool IsSet { get; init; }
    public string? OperatorId { get; init; }
}
```

### QKickNotification

```csharp
public sealed record QKickNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
    public string? OperatorId { get; init; }
}
```

### QQuitNotification

```csharp
public sealed record QQuitNotification : QGroupNotification
{
    public required string TargetUserId { get; init; }
}
```

### QReactionType

```csharp
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
public sealed record QLoginInfo(string Uin, string Nickname);
```

### QImplInfo

```csharp
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
public sealed record QMemberMute
{
    public required string UserId { get; init; }
    public required TimeSpan Duration { get; init; }
}
```

### QOperationStatus

```csharp
public enum QOperationStatus { Succeeded, Failed, Unknown, NotExecuted }
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
public sealed record QBatchOperationResult
{
    public IReadOnlyList<QOperationResult> Items { get; init; } = [];
    public bool IsSuccess => Items.All(x => x.Status == QOperationStatus.Succeeded);
}
```

### QBatchOperationCanceledException

```csharp
public sealed class QBatchOperationCanceledException(QBatchOperationResult partialResult, CancellationToken token)
    : OperationCanceledException("Batch canceled; inspect PartialResult before retrying.", token)
{
    public QBatchOperationResult PartialResult { get; } = partialResult;
}
```

### QPatch

```csharp
public readonly record struct QPatch<T>
{
    public bool IsSpecified { get; private init; }
    public bool IsClear { get; private init; }
    public T? Value { get; private init; }
    public static QPatch<T> Set(T value) => value is null ? throw new ArgumentNullException(nameof(value))
        : new() { IsSpecified = true, Value = value };
    public static QPatch<T> Clear() => new() { IsSpecified = true, IsClear = true };
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
public sealed record QMuteRecurring(string TaskId, IReadOnlyList<int> Weekdays, string? StartTime, string? EndTime, bool Enabled);
```

### QGroupMuteState

```csharp
public sealed record QGroupMuteState(string? Mode, IReadOnlyList<QMuteSchedule> Schedules,
    IReadOnlyList<QMuteRecurring> RecurringRules, IReadOnlyList<QMutedMember> Members);
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

## 原生消息

### QIncomingMessage

```csharp
public abstract record QIncomingMessage
{
    public required string PeerId { get; init; }
    public required string MessageId { get; init; }
    public required string SenderId { get; init; }
    public DateTimeOffset Time { get; init; }
    public IReadOnlyList<QIncomingSegment> Segments { get; init; } = [];

    public abstract QMessageScene Scene { get; }

    public string GetPlainText() =>
        string.Concat(Segments.OfType<QIncomingText>().Select(segment => segment.Text));
}
```

### QFriendMessage

```csharp
public sealed record QFriendMessage : QIncomingMessage
{
    public required QFriend Friend { get; init; }
    public override QMessageScene Scene => QMessageScene.Friend;
}
```

### QGroupMessage

```csharp
public sealed record QGroupMessage : QIncomingMessage
{
    public required QGroup Group { get; init; }
    public required QGroupMember GroupMember { get; init; }
    public override QMessageScene Scene => QMessageScene.Group;
}
```

### QTempMessage

```csharp
public sealed record QTempMessage : QIncomingMessage
{
    public QGroup? Group { get; init; }
    public override QMessageScene Scene => QMessageScene.Temp;
}
```

### QForwardedIncomingMessage

```csharp
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
public sealed record QSentMessage(string MessageId, DateTimeOffset Time);
```

## 入站与出站消息段

### QIncomingSegment

```csharp
public abstract record QIncomingSegment;
```

### QIncomingText

```csharp
public sealed record QIncomingText(string Text) : QIncomingSegment;
```

### QIncomingMention

```csharp
public sealed record QIncomingMention(string UserId, string Name) : QIncomingSegment;
```

### QIncomingMentionAll

```csharp
public sealed record QIncomingMentionAll : QIncomingSegment;
```

### QIncomingFace

```csharp
public sealed record QIncomingFace(string FaceId, bool IsLarge = false) : QIncomingSegment;
```

### QIncomingReply

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

```csharp
public sealed record QIncomingRecord(string ResourceId, string TempUrl) : QIncomingSegment
{
    public TimeSpan Duration { get; init; }
}
```

### QIncomingVideo

```csharp
public sealed record QIncomingVideo(string ResourceId, string TempUrl) : QIncomingSegment
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### QIncomingFile

```csharp
public sealed record QIncomingFile(string FileId, string FileName, long FileSize) : QIncomingSegment
{
    public string? FileHash { get; init; }
}
```

### QIncomingForward

```csharp
public sealed record QIncomingForward(string ForwardId) : QIncomingSegment
{
    public string? Title { get; init; }
    public IReadOnlyList<string> Preview { get; init; } = [];
    public string? Summary { get; init; }
}
```

### QIncomingMarketFace

```csharp
public sealed record QIncomingMarketFace(string EmojiId, string Url) : QIncomingSegment
{
    public int EmojiPackageId { get; init; }
    public string? Key { get; init; }
    public string? Summary { get; init; }
}
```

### QIncomingLightApp

```csharp
public sealed record QIncomingLightApp(string AppName, string JsonPayload) : QIncomingSegment;
```

### QIncomingXml

```csharp
public sealed record QIncomingXml(int ServiceId, string XmlPayload) : QIncomingSegment;
```

### QIncomingMarkdown

```csharp
public sealed record QIncomingMarkdown(string Content) : QIncomingSegment;
```

### QOutgoingSegment

```csharp
public abstract record QOutgoingSegment;
```

### QOutgoingText

```csharp
public sealed record QOutgoingText(string Text) : QOutgoingSegment;
```

### QOutgoingMention

```csharp
public sealed record QOutgoingMention(string UserId) : QOutgoingSegment;
```

### QOutgoingMentionAll

```csharp
public sealed record QOutgoingMentionAll : QOutgoingSegment;
```

### QOutgoingFace

```csharp
public sealed record QOutgoingFace(string FaceId, bool IsLarge = false) : QOutgoingSegment;
```

### QOutgoingReply

```csharp
public sealed record QOutgoingReply(string MessageId) : QOutgoingSegment;
```

### QOutgoingImage

```csharp
public sealed record QOutgoingImage(string Uri) : QOutgoingSegment
{
    public string? Summary { get; init; }

    /// <summary>协议自定义子类型(如 normal / sticker)。</summary>
    public string? SubType { get; init; }
}
```

### QOutgoingRecord

```csharp
public sealed record QOutgoingRecord(string Uri) : QOutgoingSegment;
```

### QOutgoingVideo

```csharp
public sealed record QOutgoingVideo(string Uri) : QOutgoingSegment
{
    public string? ThumbUri { get; init; }
}
```

### QOutgoingLightApp

```csharp
public sealed record QOutgoingLightApp(string JsonPayload) : QOutgoingSegment;
```

### QForwardedMessage

```csharp
public sealed record QForwardedMessage(string UserId, string SenderName, IReadOnlyList<QOutgoingSegment> Segments)
{
    /// <summary>消息展示时间,null 使用当前时间。</summary>
    public DateTimeOffset? Time { get; init; }
}
```

### QOutgoingForward

```csharp
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
}
```

### QOfficialButtonInteraction

```csharp
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

### QFriendFileUpload

```csharp
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
public sealed record QGroupNameChange : QEventPayload
{
    public required string GroupId { get; init; }
    public required string NewGroupName { get; init; }
    public string? OperatorId { get; init; }
}
```

### QGroupMessageReaction

```csharp
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
public sealed record QGroupWholeMute : QEventPayload
{
    public required string GroupId { get; init; }
    public string? OperatorId { get; init; }
    public bool IsMute { get; init; }
}
```

### QGroupNudge

```csharp
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
public sealed record QGroupDisband : QEventPayload
{
    public required string GroupId { get; init; }
    public string? OperatorId { get; init; }
}
```

### QPeerPinChange

```csharp
public sealed record QPeerPinChange : QEventPayload
{
    public required QMessageScene Scene { get; init; }
    public required string PeerId { get; init; }
    public bool IsPinned { get; init; }
}
```

### QMessageRecall

```csharp
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
public sealed record QBotOffline : QEventPayload
{
    public string? Reason { get; init; }
}
```

## 官方消息、Markdown、按钮与媒体

### QOfficialMessageTarget

```csharp
public sealed record QOfficialMessageTarget(QOfficialMessageScene Scene, string Id);
```

### QOfficialMessage

```csharp
public abstract record QOfficialMessage
{
    public static QOfficialMessage Text(string content) => new QOfficialTextMessage(content);

    public static QOfficialMessage Markdown(QOfficialMarkdown markdown, QOfficialKeyboard? keyboard = null) =>
        new QOfficialMarkdownMessage(markdown, keyboard);

    /// <summary>创建本地媒体消息；媒体上传由适配器在发送时完成。</summary>
    public static QOfficialMessage Media(QOfficialMediaType type, Stream content, string fileName,
        string? caption = null) => new QOfficialMediaSourceMessage(type, content, fileName, caption);

    /// <summary>创建已上传媒体消息，可在被动回复时复用 file_info。</summary>
    public static QOfficialMessage Media(QOfficialMedia media, string? caption = null) =>
        new QOfficialUploadedMediaMessage(media, caption);
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

```csharp
public abstract record QOfficialMarkdown
{
    /// <summary>图片转存失败时拒绝发送整条消息。</summary>
    public bool? ForceVerifyImageResource { get; init; }
}
```

### QCustomMarkdown

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

```csharp
public abstract record QOfficialKeyboard;
```

### QKeyboardTemplate

```csharp
public sealed record QKeyboardTemplate(string Id) : QOfficialKeyboard;
```

### QInlineKeyboard

```csharp
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
public sealed record QOfficialMessageReply
{
    public string? MessageId { get; init; }
    public string? EventId { get; init; }
    public int? MessageSequence { get; init; }
}
```

### QOfficialMediaType

```csharp
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
    Members = 1 << 21
}
```
