namespace ShiroBot.Qq.Model;

/// <summary>
/// QQ 好友扩展服务。插件通过 context.GetAdapterExtension&lt;IQqFriendApi&gt;() 探测。
/// </summary>
public interface IQqFriendApi
{
    Task SendNudgeAsync(long userId, bool isSelf = false)
        => throw new NotSupportedException();

    Task SendProfileLikeAsync(long userId, int count = 1)
        => throw new NotSupportedException();

    Task DeleteFriendAsync(long userId)
        => throw new NotSupportedException();
}

/// <summary>QQ 群管理扩展服务。</summary>
public interface IQqGroupApi
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

    Task<IReadOnlyList<QqGroupAnnouncement>> GetAnnouncementsAsync(long groupId)
        => throw new NotSupportedException();

    Task SendAnnouncementAsync(long groupId, string content, string? imageUri = null)
        => throw new NotSupportedException();

    Task DeleteAnnouncementAsync(long groupId, string announcementId)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QqEssenceMessage>> GetEssenceMessagesAsync(long groupId, int pageIndex, int pageSize)
        => throw new NotSupportedException();

    Task SetEssenceMessageAsync(long groupId, long messageSeq, bool isSet = true)
        => throw new NotSupportedException();

    Task AcceptJoinRequestAsync(QqGroupJoinRequest request)
        => throw new NotSupportedException();

    Task RejectJoinRequestAsync(QqGroupJoinRequest request, string? reason = null)
        => throw new NotSupportedException();
}

/// <summary>QQ 群文件扩展服务。</summary>
public interface IQqFileApi
{
    Task<string> UploadPrivateFileAsync(long userId, string fileUri, string fileName)
        => throw new NotSupportedException();

    Task<string> UploadGroupFileAsync(long groupId, string fileUri, string fileName, string parentFolderId = "/")
        => throw new NotSupportedException();

    Task<string> GetPrivateFileDownloadUrlAsync(long userId, string fileId, string fileHash)
        => throw new NotSupportedException();

    Task<string> GetGroupFileDownloadUrlAsync(long groupId, string fileId)
        => throw new NotSupportedException();

    Task<(IReadOnlyList<QqGroupFile> Files, IReadOnlyList<QqGroupFolder> Folders)> GetGroupFilesAsync(
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
}

/// <summary>QQ 账号/资料扩展服务。</summary>
public interface IQqSystemApi
{
    Task<QqUserProfile> GetUserProfileAsync(long userId)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QqFriend>> GetFriendListAsync(bool noCache = false)
        => throw new NotSupportedException();

    Task<QqFriend> GetFriendInfoAsync(long userId, bool noCache = false)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QqGroup>> GetGroupListAsync(bool noCache = false)
        => throw new NotSupportedException();

    Task<QqGroup> GetGroupInfoAsync(long groupId, bool noCache = false)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QqGroupMember>> GetGroupMemberListAsync(long groupId, bool noCache = false)
        => throw new NotSupportedException();

    Task<QqGroupMember> GetGroupMemberInfoAsync(long groupId, long userId, bool noCache = false)
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
}

/// <summary>QQ 消息扩展服务(合并转发、原生段收发)。</summary>
public interface IQqMessageApi
{
    /// <summary>用 QQ 原生段发送消息(LightApp、合并转发等核心模型未覆盖的内容)。</summary>
    Task<long> SendMessageAsync(QqMessageScene scene, long peerId, IReadOnlyList<QqOutgoingSegment> segments)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QqForwardedIncomingMessage>> GetForwardedMessagesAsync(string forwardId)
        => throw new NotSupportedException();

    Task MarkAsReadAsync(QqMessageScene scene, long peerId, long messageSeq)
        => throw new NotSupportedException();
}
