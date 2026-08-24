namespace ShiroBot.Model.QQ;

/// <summary>
/// QQ 好友扩展服务。插件通过 context.GetAdapterExtension&lt;IQFriendApi&gt;() 探测。
/// </summary>
public interface IQFriendApi
{
    Task SendNudgeAsync(long userId, bool isSelf = false)
        => throw new NotSupportedException();

    Task SendProfileLikeAsync(long userId, int count = 1)
        => throw new NotSupportedException();

    Task DeleteFriendAsync(long userId)
        => throw new NotSupportedException();

    /// <summary>获取好友请求列表。</summary>
    Task<IReadOnlyList<QFriendRequest>> GetFriendRequestsAsync(int limit = 20, bool isFiltered = false)
        => throw new NotSupportedException();

    /// <summary>接受好友请求。initiatorUid 来自 QFriendRequest.InitiatorUid 或好友请求事件的 Token。</summary>
    Task AcceptFriendRequestAsync(string initiatorUid, bool isFiltered = false)
        => throw new NotSupportedException();

    Task RejectFriendRequestAsync(string initiatorUid, bool isFiltered = false, string? reason = null)
        => throw new NotSupportedException();
}

/// <summary>QQ 群管理扩展服务。</summary>
public interface IQGroupApi
{
    Task SetGroupNameAsync(long groupId, string name)
        => throw new NotSupportedException();

    Task SetGroupAvatarAsync(long groupId, string imageUri)
        => throw new NotSupportedException();

    Task SetMemberCardAsync(long groupId, long userId, string card)
        => throw new NotSupportedException();

    Task SetMemberSpecialTitleAsync(long groupId, long userId, string title)
        => throw new NotSupportedException();

    Task SetMemberAdminAsync(long groupId, long userId, bool isSet = true)
        => throw new NotSupportedException();

    Task MuteMemberAsync(long groupId, long userId, TimeSpan duration)
        => throw new NotSupportedException();

    Task SetWholeMuteAsync(long groupId, bool isMute = true)
        => throw new NotSupportedException();

    Task KickMemberAsync(long groupId, long userId, bool rejectAddRequest = false)
        => throw new NotSupportedException();

    Task QuitGroupAsync(long groupId)
        => throw new NotSupportedException();

    Task SendNudgeAsync(long groupId, long userId)
        => throw new NotSupportedException();

    Task SendMessageReactionAsync(long groupId, long messageSeq, string faceId, bool isAdd = true)
        => throw new NotSupportedException();

    /// <summary>发送消息表情回应(指定 Face/Emoji 类型)。</summary>
    Task SendMessageReactionAsync(long groupId, long messageSeq, string reactionId, QReactionType reactionType, bool isAdd = true)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QGroupAnnouncement>> GetAnnouncementsAsync(long groupId)
        => throw new NotSupportedException();

    Task SendAnnouncementAsync(long groupId, string content, string? imageUri = null)
        => throw new NotSupportedException();

    Task DeleteAnnouncementAsync(long groupId, string announcementId)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QEssenceMessage>> GetEssenceMessagesAsync(long groupId, int pageIndex, int pageSize)
        => throw new NotSupportedException();

    /// <summary>获取一页群精华消息，并返回是否已到最后一页。</summary>
    Task<(IReadOnlyList<QEssenceMessage> Messages, bool IsEnd)> GetEssenceMessagesPageAsync(
        long groupId, int pageIndex, int pageSize)
        => throw new NotSupportedException();

    Task SetEssenceMessageAsync(long groupId, long messageSeq, bool isSet = true)
        => throw new NotSupportedException();

    Task AcceptJoinRequestAsync(QGroupJoinRequest request)
        => throw new NotSupportedException();

    Task RejectJoinRequestAsync(QGroupJoinRequest request, string? reason = null)
        => throw new NotSupportedException();

    /// <summary>按通知序号接受入群申请/邀请入群申请。</summary>
    Task AcceptJoinRequestAsync(long groupId, long notificationSeq, bool isInvited = false, bool isFiltered = false)
        => throw new NotSupportedException();

    Task RejectJoinRequestAsync(long groupId, long notificationSeq, bool isInvited = false, bool isFiltered = false, string? reason = null)
        => throw new NotSupportedException();

    /// <summary>获取群通知列表(入群申请/邀请/管理员变更/踢人/退群)。返回通知与下一页起始序号。</summary>
    Task<(IReadOnlyList<QGroupNotification> Notifications, long? NextNotificationSeq)> GetNotificationsAsync(
        long? startNotificationSeq = null, bool isFiltered = false, int limit = 20)
        => throw new NotSupportedException();

    /// <summary>接受他人邀请机器人入群。invitationSeq 来自群邀请事件的 Token。</summary>
    Task AcceptInvitationAsync(long groupId, long invitationSeq)
        => throw new NotSupportedException();

    Task RejectInvitationAsync(long groupId, long invitationSeq)
        => throw new NotSupportedException();
}

/// <summary>QQ 群文件扩展服务。</summary>
public interface IQFileApi
{
    Task<string> UploadPrivateFileAsync(long userId, string fileUri, string fileName)
        => throw new NotSupportedException();

    Task<string> UploadGroupFileAsync(long groupId, string fileUri, string fileName, string parentFolderId = "/")
        => throw new NotSupportedException();

    Task<string> GetPrivateFileDownloadUrlAsync(long userId, string fileId, string fileHash)
        => throw new NotSupportedException();

    /// <summary>获取私聊文件下载链接，并指定文件是否由机器人自己发送。</summary>
    Task<string> GetPrivateFileDownloadUrlAsync(long userId, string fileId, string fileHash, bool isSelfSend)
    {
        if (isSelfSend)
            throw new NotSupportedException("Current adapter does not support downloading self-sent private files.");

        return GetPrivateFileDownloadUrlAsync(userId, fileId, fileHash);
    }

    Task<string> GetGroupFileDownloadUrlAsync(long groupId, string fileId)
        => throw new NotSupportedException();

    Task<(IReadOnlyList<QGroupFile> Files, IReadOnlyList<QGroupFolder> Folders)> GetGroupFilesAsync(
        long groupId, string parentFolderId = "/")
        => throw new NotSupportedException();

    Task MoveGroupFileAsync(long groupId, string fileId, string targetFolderId, string parentFolderId = "/")
        => throw new NotSupportedException();

    Task RenameGroupFileAsync(long groupId, string fileId, string newFileName, string parentFolderId = "/")
        => throw new NotSupportedException();

    Task DeleteGroupFileAsync(long groupId, string fileId)
        => throw new NotSupportedException();

    Task<string> CreateGroupFolderAsync(long groupId, string folderName)
        => throw new NotSupportedException();

    Task RenameGroupFolderAsync(long groupId, string folderId, string newFolderName)
        => throw new NotSupportedException();

    Task DeleteGroupFolderAsync(long groupId, string folderId)
        => throw new NotSupportedException();

    /// <summary>把群文件转存为永久文件(阻止过期)。</summary>
    Task PersistGroupFileAsync(long groupId, string fileId)
        => throw new NotSupportedException();
}

/// <summary>QQ 账号/资料扩展服务。</summary>
public interface IQSystemApi
{
    Task<QUserProfile> GetUserProfileAsync(long userId)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QFriend>> GetFriendListAsync(bool noCache = false)
        => throw new NotSupportedException();

    Task<QFriend> GetFriendInfoAsync(long userId, bool noCache = false)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QGroup>> GetGroupListAsync(bool noCache = false)
        => throw new NotSupportedException();

    Task<QGroup> GetGroupInfoAsync(long groupId, bool noCache = false)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QGroupMember>> GetGroupMemberListAsync(long groupId, bool noCache = false)
        => throw new NotSupportedException();

    Task<QGroupMember> GetGroupMemberInfoAsync(long groupId, long userId, bool noCache = false)
        => throw new NotSupportedException();

    /// <summary>获取置顶的好友和群。</summary>
    Task<(IReadOnlyList<QFriend> Friends, IReadOnlyList<QGroup> Groups)> GetPeerPinsAsync()
        => throw new NotSupportedException();

    Task SetAvatarAsync(string imageUri)
        => throw new NotSupportedException();

    Task SetNicknameAsync(string nickname)
        => throw new NotSupportedException();

    Task SetBioAsync(string bio)
        => throw new NotSupportedException();

    Task<string> GetCookiesAsync(string domain)
        => throw new NotSupportedException();

    Task<string> GetCsrfTokenAsync()
        => throw new NotSupportedException();

    /// <summary>获取登录账号信息。</summary>
    Task<QLoginInfo> GetLoginInfoAsync()
        => throw new NotSupportedException();

    /// <summary>获取协议实现端信息(实现名/版本/QQ协议类型)。</summary>
    Task<QImplInfo> GetImplInfoAsync()
        => throw new NotSupportedException();

    /// <summary>获取收藏表情 URL 列表。</summary>
    Task<IReadOnlyList<string>> GetCustomFaceUrlListAsync()
        => throw new NotSupportedException();

    /// <summary>设置会话置顶。</summary>
    Task SetPeerPinAsync(QMessageScene scene, long peerId, bool isPinned = true)
        => throw new NotSupportedException();
}

/// <summary>QQ 消息扩展服务(合并转发、原生段收发)。</summary>
public interface IQMessageApi
{
    /// <summary>用 QQ 原生段发送消息(LightApp、合并转发等核心模型未覆盖的内容)。</summary>
    Task<long> SendMessageAsync(QMessageScene scene, long peerId, IReadOnlyList<QOutgoingSegment> segments)
        => throw new NotSupportedException();

    /// <summary>发送 QQ 原生消息，并返回消息序列号和发送时间。</summary>
    Task<QSentMessage> SendMessageDetailedAsync(
        QMessageScene scene, long peerId, IReadOnlyList<QOutgoingSegment> segments)
        => throw new NotSupportedException();

    /// <summary>获取单条消息(QQ 原生形态)。</summary>
    Task<QIncomingMessage?> GetMessageAsync(QMessageScene scene, long peerId, long messageSeq)
        => throw new NotSupportedException();

    /// <summary>获取历史消息(QQ 原生形态)。返回消息与下一页起始序号。</summary>
    Task<(IReadOnlyList<QIncomingMessage> Messages, long? NextMessageSeq)> GetHistoryMessagesAsync(
        QMessageScene scene, long peerId, long? startMessageSeq = null, int limit = 20)
        => throw new NotSupportedException();

    /// <summary>撤回消息。</summary>
    Task RecallMessageAsync(QMessageScene scene, long peerId, long messageSeq)
        => throw new NotSupportedException();

    /// <summary>把接收到的资源 ID 解析为临时下载 URL。</summary>
    Task<string> GetResourceTempUrlAsync(string resourceId)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QForwardedIncomingMessage>> GetForwardedMessagesAsync(string forwardId)
        => throw new NotSupportedException();

    Task MarkAsReadAsync(QMessageScene scene, long peerId, long messageSeq)
        => throw new NotSupportedException();
}
