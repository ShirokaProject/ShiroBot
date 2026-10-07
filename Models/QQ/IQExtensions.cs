namespace ShiroBot.Model.QQ;

/// <summary>
/// QQ 好友扩展服务。插件通过 context.GetAdapterExtension&lt;IQFriendApi&gt;() 探测。
/// </summary>
public interface IQFriendApi
{
    Task SendNudgeAsync(string userId, bool isSelf = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SendProfileLikeAsync(string userId, int count = 1, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task DeleteFriendAsync(string userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取好友请求列表。</summary>
    Task<IReadOnlyList<QFriendRequest>> GetFriendRequestsAsync(int limit = 20, bool isFiltered = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>接受好友请求。initiatorUid 来自 QFriendRequest.InitiatorUid 或好友请求事件的 Token。</summary>
    Task AcceptFriendRequestAsync(string initiatorUid, bool isFiltered = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task RejectFriendRequestAsync(string initiatorUid, bool isFiltered = false, string? reason = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>QQ 群管理扩展服务。</summary>
public interface IQGroupApi
{
    /// <summary>适配器实现的能力，不代表账号已获得平台授权。</summary>
    QGroupCapabilities Capabilities => QGroupCapabilities.None;
    Task<IReadOnlyList<QGroup>> GetGroupListAsync(bool noCache = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<QGroup> GetGroupInfoAsync(string groupId, bool noCache = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QGroupMember>> GetGroupMemberListAsync(string groupId, bool noCache = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<QGroupMember> GetGroupMemberInfoAsync(string groupId, string userId, bool noCache = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetGroupNameAsync(string groupId, string name, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetGroupAvatarAsync(string groupId, string imageUri, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetMemberCardAsync(string groupId, string userId, string card, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetMemberSpecialTitleAsync(string groupId, string userId, string title, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetMemberAdminAsync(string groupId, string userId, bool isSet = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task MuteMemberAsync(string groupId, string userId, TimeSpan duration, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetWholeMuteAsync(string groupId, bool isMute = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task KickMemberAsync(string groupId, string userId, bool rejectAddRequest = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task QuitGroupAsync(string groupId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SendNudgeAsync(string groupId, string userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SendMessageReactionAsync(string groupId, string messageId, string faceId, bool isAdd = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>发送消息表情回应(指定 Face/Emoji 类型)。</summary>
    Task SendMessageReactionAsync(string groupId, string messageId, string reactionId, QReactionType reactionType, bool isAdd = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QGroupAnnouncement>> GetAnnouncementsAsync(string groupId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SendAnnouncementAsync(string groupId, string content, string? imageUri = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task DeleteAnnouncementAsync(string groupId, string announcementId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QEssenceMessage>> GetEssenceMessagesAsync(string groupId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取一页群精华消息，并返回是否已到最后一页。</summary>
    Task<(IReadOnlyList<QEssenceMessage> Messages, bool IsEnd)> GetEssenceMessagesPageAsync(
        string groupId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetEssenceMessageAsync(string groupId, string messageId, bool isSet = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>按原样返回的申请对象接受申请或邀请他人入群。</summary>
    Task AcceptJoinRequestAsync(QGroupJoinRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task RejectJoinRequestAsync(QGroupJoinRequest request, string? reason = null, bool addToBlacklist = false,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>统一分页申请列表；Cursor 是不透明分页凭据。</summary>
    Task<QGroupJoinRequestPage> GetJoinRequestsAsync(string groupId, string? cursor = null,
        int limit = 20, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<QGroupMuteState> GetMuteStateAsync(string groupId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<QBatchOperationResult> SetMemberMutesAsync(string groupId, IReadOnlyList<QMemberMute> members,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取群通知列表(入群申请/邀请/管理员变更/踢人/退群)。返回通知与下一页起始序号。</summary>
    Task<(IReadOnlyList<QGroupNotification> Notifications, string? NextCursor)> GetNotificationsAsync(
        string? cursor = null, bool isFiltered = false, int limit = 20, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>接受他人邀请机器人入群。invitationId 来自群邀请事件的 Token。</summary>
    Task AcceptInvitationAsync(string groupId, string invitationId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task RejectInvitationAsync(string groupId, string invitationId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>QQ 群文件扩展服务。</summary>
public interface IQFileApi
{
    Task<string> UploadPrivateFileAsync(string userId, string fileUri, string fileName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<string> UploadGroupFileAsync(string groupId, string fileUri, string fileName, string parentFolderId = "/", CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<string> GetPrivateFileDownloadUrlAsync(string userId, string fileId, string fileHash, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取私聊文件下载链接，并指定文件是否由机器人自己发送。</summary>
    Task<string> GetPrivateFileDownloadUrlAsync(string userId, string fileId, string fileHash, bool isSelfSend, CancellationToken cancellationToken = default)
    {
        if (isSelfSend)
            throw new NotSupportedException("Current adapter does not support downloading self-sent private files.");

        return GetPrivateFileDownloadUrlAsync(userId, fileId, fileHash, cancellationToken);
    }

    Task<string> GetGroupFileDownloadUrlAsync(string groupId, string fileId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<(IReadOnlyList<QGroupFile> Files, IReadOnlyList<QGroupFolder> Folders)> GetGroupFilesAsync(
        string groupId, string parentFolderId = "/", CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task MoveGroupFileAsync(string groupId, string fileId, string targetFolderId, string parentFolderId = "/", CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task RenameGroupFileAsync(string groupId, string fileId, string newFileName, string parentFolderId = "/", CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task DeleteGroupFileAsync(string groupId, string fileId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<string> CreateGroupFolderAsync(string groupId, string folderName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task RenameGroupFolderAsync(string groupId, string folderId, string newFolderName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task DeleteGroupFolderAsync(string groupId, string folderId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>把群文件转存为永久文件(阻止过期)。</summary>
    Task PersistGroupFileAsync(string groupId, string fileId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>QQ 账号/资料扩展服务。</summary>
public interface IQSystemApi
{
    Task<QUserProfile> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QFriend>> GetFriendListAsync(bool noCache = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<QFriend> GetFriendInfoAsync(string userId, bool noCache = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取置顶的好友和群。</summary>
    Task<(IReadOnlyList<QFriend> Friends, IReadOnlyList<QGroup> Groups)> GetPeerPinsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetAvatarAsync(string imageUri, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetNicknameAsync(string nickname, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task SetBioAsync(string bio, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<string> GetCookiesAsync(string domain, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<string> GetCsrfTokenAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取登录账号信息。</summary>
    Task<QLoginInfo> GetLoginInfoAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取协议实现端信息(实现名/版本/QQ协议类型)。</summary>
    Task<QImplInfo> GetImplInfoAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取收藏表情 URL 列表。</summary>
    Task<IReadOnlyList<string>> GetCustomFaceUrlListAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>设置会话置顶。</summary>
    Task SetPeerPinAsync(QMessageScene scene, string peerId, bool isPinned = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>QQ 消息扩展服务(合并转发、原生段收发)。</summary>
public interface IQMessageApi
{
    /// <summary>用 QQ 原生段发送消息(LightApp、合并转发等核心模型未覆盖的内容)。</summary>
    Task<string> SendMessageAsync(QMessageScene scene, string peerId, IReadOnlyList<QOutgoingSegment> segments, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>发送 QQ 原生消息，并返回消息序列号和发送时间。</summary>
    Task<QSentMessage> SendMessageDetailedAsync(
        QMessageScene scene, string peerId, IReadOnlyList<QOutgoingSegment> segments, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取单条消息(QQ 原生形态)。</summary>
    Task<QIncomingMessage?> GetMessageAsync(QMessageScene scene, string peerId, string messageId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>获取历史消息(QQ 原生形态)。返回消息与下一页起始序号。</summary>
    Task<(IReadOnlyList<QIncomingMessage> Messages, string? NextCursor)> GetHistoryMessagesAsync(
        QMessageScene scene, string peerId, string? cursor = null, int limit = 20, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>撤回消息。</summary>
    Task RecallMessageAsync(QMessageScene scene, string peerId, string messageId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>把接收到的资源 ID 解析为临时下载 URL。</summary>
    Task<string> GetResourceTempUrlAsync(string resourceId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<IReadOnlyList<QForwardedIncomingMessage>> GetForwardedMessagesAsync(string forwardId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task MarkAsReadAsync(QMessageScene scene, string peerId, string messageId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// QQ 官方开放平台的 Markdown、按钮、Embed 与 Ark 消息能力。插件通过
/// context.GetAdapterExtension&lt;IQOfficialMessageApi&gt;() 探测；
/// 未实现此接口的适配器不声明该能力。
/// </summary>
public interface IQOfficialMessageApi
{
    /// <summary>发送一条类型化 QQ 官方消息；媒体类型需要适配器同时实现 IQOfficialMediaApi。</summary>
    async Task<string> SendAsync(
        QOfficialMessageTarget target,
        QOfficialMessage message,
        QOfficialMessageReply? reply = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(message);
        return message switch
        {
            QOfficialTextMessage text => await SendTextAsync(target, text.Content, reply, cancellationToken).ConfigureAwait(false),
            QOfficialMarkdownMessage markdown => await SendMarkdownAsync(target, markdown.Content,
                markdown.Keyboard, reply, cancellationToken).ConfigureAwait(false),
            QOfficialMediaSourceMessage media when this is IQOfficialMediaApi mediaApi =>
                media.Caption is null
                    ? await mediaApi.UploadAndSendAsync(target, media.Type, media.Content, media.FileName,
                        reply, cancellationToken).ConfigureAwait(false)
                    : await mediaApi.UploadAndSendWithCaptionAsync(target, media.Type, media.Content, media.FileName,
                        media.Caption, reply, cancellationToken).ConfigureAwait(false),
            QOfficialUploadedMediaMessage media when this is IQOfficialMediaApi mediaApi =>
                media.Caption is null
                    ? await mediaApi.SendAsync(target, media.UploadedMedia, reply, cancellationToken).ConfigureAwait(false)
                    : await mediaApi.SendWithCaptionAsync(target, media.UploadedMedia, media.Caption, reply, cancellationToken)
                        .ConfigureAwait(false),
            _ => throw new NotSupportedException("This adapter does not support the requested QQ official message type.")
        };
    }

    /// <summary>发送官方文本消息，可通过 reply 指定消息或事件被动回复。</summary>
    Task<string> SendTextAsync(
        QOfficialMessageTarget target,
        string content,
        QOfficialMessageReply? reply = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>发送 QQ 官方 Ark 模板消息；不支持时抛出 NotSupportedException。</summary>
    Task<string> SendArkAsync(
        QOfficialMessageTarget target,
        int templateId,
        IReadOnlyDictionary<string, string> fields,
        QOfficialMessageReply? reply = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>发送 QQ 官方 Embed 卡片消息。</summary>
    Task<string> SendEmbedAsync(
        QOfficialMessageTarget target,
        QOfficialEmbed embed,
        QOfficialMessageReply? reply = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>
    /// 探测此目标和按钮形式是否被适配器支持。平台权限可能变化，
    /// 返回 true 不保证后续发送一定成功。
    /// </summary>
    bool CanSendMarkdown(
        QOfficialMessageTarget target,
        QOfficialMarkdown markdown,
        QOfficialKeyboard? keyboard = null);

    /// <summary>
    /// 发送 Markdown，可附带底部按钮。返回官方消息 ID。
    /// 自定义按钮是否可用取决于机器人开放平台权限和发送场景。
    /// </summary>
    Task<string> SendMarkdownAsync(
        QOfficialMessageTarget target,
        QOfficialMarkdown markdown,
        QOfficialKeyboard? keyboard = null,
        QOfficialMessageReply? reply = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 回应消息按钮互动。每个 InteractionId 仅能回应一次且会过期；
    /// 适配器应及时调用，避免等待耗时的插件处理使客户端一直显示加载。
    /// </summary>
    Task AcknowledgeInteractionAsync(
        string interactionId,
        QOfficialInteractionResponseCode code = QOfficialInteractionResponseCode.Success, CancellationToken cancellationToken = default);
}

/// <summary>QQ 官方群媒体上传与发送能力。媒体上传使用可读流，避免插件依赖适配器实现。</summary>
public interface IQOfficialMediaApi
{
    /// <summary>上传本地媒体并返回可复用的官方 file_info。</summary>
    Task<QOfficialMedia> UploadAsync(
        QOfficialMessageTarget target,
        QOfficialMediaType type,
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>发送已上传媒体；可传入消息或事件用于被动回复。</summary>
    Task<string> SendAsync(
        QOfficialMessageTarget target,
        QOfficialMedia media,
        QOfficialMessageReply? reply = null, CancellationToken cancellationToken = default);

    /// <summary>发送官方媒体并附带文本说明；图片说明可通过同一条富媒体消息发送。</summary>
    Task<string> SendWithCaptionAsync(
        QOfficialMessageTarget target,
        QOfficialMedia media,
        string content,
        QOfficialMessageReply? reply = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>上传并发送媒体。没有 reply 时使用平台主动发送接口。</summary>
    Task<string> UploadAndSendAsync(
        QOfficialMessageTarget target,
        QOfficialMediaType type,
        Stream content,
        string fileName,
        QOfficialMessageReply? reply = null,
        CancellationToken cancellationToken = default);

    /// <summary>上传媒体并在同一条富媒体消息中附带文本说明。</summary>
    Task<string> UploadAndSendWithCaptionAsync(
        QOfficialMessageTarget target,
        QOfficialMediaType type,
        Stream content,
        string fileName,
        string caption,
        QOfficialMessageReply? reply = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// QQ 官方私聊输入状态与流式回复。插件通过
/// context.GetAdapterExtension&lt;IQOfficialDirectMessageApi&gt;() 探测。
/// </summary>
public interface IQOfficialDirectMessageApi
{
    Task SendTypingAsync(QOfficialMessageTarget target, QOfficialMessageReply reply, TimeSpan duration, CancellationToken cancellationToken = default);

    IQOfficialMessageStream BeginStream(
        QOfficialMessageTarget target,
        QOfficialMessageReply reply,
        QOfficialStreamContentType contentType = QOfficialStreamContentType.Text);
}

/// <summary>流式回复；AppendAsync 接收截至当前的完整可见文本。</summary>
public interface IQOfficialMessageStream : IAsyncDisposable
{
    bool HasStarted { get; }
    Task AppendAsync(string cumulativeText, CancellationToken cancellationToken = default);
    Task<string> CompleteAsync(CancellationToken cancellationToken = default);
}
