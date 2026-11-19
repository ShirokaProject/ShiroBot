using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Discord;

// 插件通过 Context.GetAdapterExtension<T>() 探测这些接口。官方机器人适配器与用户账号适配器共用本 Model（平台 ID 均为 discord），
// 只实现各自账号能用的接口：标注“仅机器人”或“仅用户账号”的接口在另一种适配器上不可用，插件会拿到 null。
// ID 参数使用 Snowflake 字符串，与 SDK 的 User.Id、Channel.Id、MessageId 相同；消息方法的 Channel 为 Direct 时 Id 为用户 ID，
// 适配器自动打开私聊。reason 写入服务器审计日志。未实现的方法默认抛 NotSupportedException；权限由 Discord 校验。

/// <summary>适配器登录的账号类型。</summary>
public enum DiscordAccountKind
{
    /// <summary>Developer Portal 创建的机器人（Bot Token）。</summary>
    Bot,

    /// <summary>
    ///     用户自己的账号。Discord 服务条款禁止自动化用户账号（self-bot），账号可能被封禁；是否使用由部署者自行承担。
    /// </summary>
    User
}

/// <summary>当前实例的账号信息、自身资料、用户查询与在线状态，两种账号都实现。</summary>
public interface IDiscordClientApi
{
    DiscordAccountKind AccountKind => DiscordAccountKind.Bot;

    /// <summary>单个附件的上传上限（字节）。机器人默认 10 MB，服务器助力等级可提高；用户账号取决于订阅。</summary>
    long MaxUploadBytes => 10L * 1024 * 1024;

    /// <summary>当前登录账号的资料。</summary>
    Task<DiscordUser> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询任意用户的公开资料。</summary>
    Task<DiscordUser> FetchUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>通过 Gateway 更新在线状态；断线重连后由适配器重新发送。</summary>
    Task SetPresenceAsync(DiscordPresence presence, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>修改当前账号的用户名或头像；null 表示不修改。Discord 对用户名修改有频率限制。</summary>
    Task<DiscordUser> ModifyCurrentUserAsync(string? username = null, string? avatarUri = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>服务器、频道、角色、成员、封禁、邀请、表情和子区管理。</summary>
public interface IDiscordGuildApi
{
    /// <summary>查询服务器资料。</summary>
    Task<DiscordGuild> GetGuildAsync(string guildId, bool withCounts = false, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>机器人所在的全部服务器。</summary>
    Task<IReadOnlyList<DiscordGuild>> GetGuildsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询服务器的全部频道（不含子区）。</summary>
    Task<IReadOnlyList<DiscordChannel>> GetGuildChannelsAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>创建频道。</summary>
    Task<DiscordChannel> CreateChannelAsync(string guildId, DiscordChannelOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>修改频道设置；未设置的项保持不变。</summary>
    Task<DiscordChannel> ModifyChannelAsync(string channelId, DiscordChannelOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>删除频道或子区。</summary>
    Task DeleteChannelAsync(string channelId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>设置频道对某个角色或成员的权限覆盖。</summary>
    Task SetChannelPermissionAsync(string channelId, string roleOrUserId, bool isRole, DiscordPermissions allow,
        DiscordPermissions deny, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>删除频道上对某个角色或成员的权限覆盖。</summary>
    Task DeleteChannelPermissionAsync(string channelId, string roleOrUserId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询服务器角色。</summary>
    Task<IReadOnlyList<DiscordRole>> GetRolesAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>创建角色。</summary>
    Task<DiscordRole> CreateRoleAsync(string guildId, DiscordRoleOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>修改角色；未设置的项保持不变。</summary>
    Task<DiscordRole> ModifyRoleAsync(string guildId, string roleId, DiscordRoleOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>删除角色。</summary>
    Task DeleteRoleAsync(string guildId, string roleId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询成员资料，并按角色计算服务器级权限。</summary>
    Task<DiscordMember> GetGuildMemberAsync(string guildId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>按用户名或昵称前缀搜索成员，最多 1000 个。</summary>
    Task<IReadOnlyList<DiscordMember>> SearchMembersAsync(string guildId, string query, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>给成员添加角色。</summary>
    Task AddMemberRoleAsync(string guildId, string userId, string roleId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>移除成员的角色。</summary>
    Task RemoveMemberRoleAsync(string guildId, string userId, string roleId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>设置成员昵称；<paramref name="userId"/> 为 <c>@me</c> 时修改机器人自己的昵称，null 清除昵称。</summary>
    Task SetNicknameAsync(string guildId, string userId, string? nickname, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>超时（禁言）成员，最长 28 天；<paramref name="until"/> 为 null 时解除超时。</summary>
    Task TimeoutMemberAsync(string guildId, string userId, DateTimeOffset? until, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>把成员移出服务器（可再次通过邀请加入）。</summary>
    Task RemoveGuildMemberAsync(string guildId, string userId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>封禁成员，并删除其最近 <paramref name="deleteMessages"/> 内的消息（最长 7 天）。</summary>
    Task BanAsync(string guildId, string userId, TimeSpan? deleteMessages = null, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>解除封禁。</summary>
    Task UnbanAsync(string guildId, string userId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询封禁列表。</summary>
    Task<IReadOnlyList<DiscordBan>> GetBansAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询服务器的邀请。</summary>
    Task<IReadOnlyList<DiscordInvite>> GetInvitesAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>为频道创建邀请。</summary>
    Task<DiscordInvite> CreateInviteAsync(string channelId, DiscordInviteOptions? options = null, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>删除邀请。</summary>
    Task DeleteInviteAsync(string code, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询服务器自定义表情。</summary>
    Task<IReadOnlyList<DiscordEmoji>> GetEmojisAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    ///     创建子区：提供 <paramref name="messageId"/> 时从该消息创建，否则在频道中创建独立子区。
    ///     返回的子区 ID 可直接作为 SDK <see cref="ChannelType.Thread"/> 频道使用。
    /// </summary>
    Task<DiscordChannel> CreateThreadAsync(string channelId, string name, string? messageId = null, bool isPrivate = false,
        TimeSpan? autoArchive = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>让当前账号加入子区。</summary>
    Task JoinThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>让当前账号离开子区。</summary>
    Task LeaveThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>把成员加入子区。</summary>
    Task AddThreadMemberAsync(string threadId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Discord 原生消息：Embed、组件、投票、附件、编辑、置顶、批量删除和 Reaction 管理。</summary>
public interface IDiscordMessageApi
{
    /// <summary>发送 Discord 原生富消息。</summary>
    Task<DiscordSentMessage> SendAsync(Channel channel, DiscordMessageContent content,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>编辑消息；未设置的部分保持不变，空列表会清除对应内容，新附件追加到已有附件之后。</summary>
    Task<DiscordSentMessage> EditAsync(Channel channel, string messageId, DiscordMessageContent content,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>把公告频道的消息推送到关注它的频道。</summary>
    Task CrosspostAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>置顶消息。</summary>
    Task PinAsync(Channel channel, string messageId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>取消置顶。</summary>
    Task UnpinAsync(Channel channel, string messageId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询频道的全部置顶消息。</summary>
    Task<IReadOnlyList<DiscordMessage>> GetPinnedAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>批量删除 2–100 条 14 天内的消息。</summary>
    Task BulkDeleteAsync(Channel channel, IReadOnlyList<string> messageIds, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>显示“正在输入”约 10 秒，或直到机器人发送消息。</summary>
    Task TriggerTypingAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <param name="emoji">Unicode 表情，或自定义表情的 name:id。</param>
    /// <summary>查询对消息添加了某个表情的用户。</summary>
    Task<IReadOnlyList<DiscordUser>> GetReactionUsersAsync(Channel channel, string messageId, string emoji, int limit = 25,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>清除消息的全部 Reaction，或只清除 <paramref name="emoji"/>。</summary>
    Task ClearReactionsAsync(Channel channel, string messageId, string? emoji = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>提前结束机器人发起的投票。</summary>
    Task<DiscordSentMessage> EndPollAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>查询投票中选择了某个选项的用户。</summary>
    Task<IReadOnlyList<DiscordUser>> GetPollVotersAsync(Channel channel, string messageId, int answerId, int limit = 25,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
///     交互回复与应用命令，仅机器人。交互令牌有效期 15 分钟，首次回复必须在 3 秒内发出。
///     适配器自动确认交互时（<see cref="AutoAcknowledges"/>），首次回复已用延迟回复占用，只能使用
///     <see cref="EditOriginalAsync"/> 和 <see cref="FollowupAsync"/>；弹窗、自动补全和临时可见的首次回复需要关闭自动确认。
/// </summary>
public interface IDiscordInteractionApi
{
    /// <summary>应用 ID；READY 之后可用。</summary>
    string? ApplicationId => null;

    bool AutoAcknowledges => true;

    /// <summary>首次回复一条消息；<see cref="DiscordMessageFlags.Ephemeral"/> 使其只对发起者可见。</summary>
    Task RespondAsync(string interactionId, DiscordMessageContent content, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    ///     先确认，稍后用 <see cref="EditOriginalAsync"/> 给出结果。组件交互且 <paramref name="updateMessage"/> 为 true 时
    ///     不显示“正在思考”，之后编辑的是组件所在的消息。
    /// </summary>
    Task DeferAsync(string interactionId, bool ephemeral = false, bool updateMessage = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>组件交互：直接修改组件所在的消息。</summary>
    Task UpdateMessageAsync(string interactionId, DiscordMessageContent content, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>弹出表单；不能用于弹窗提交和自动补全交互。</summary>
    Task ShowModalAsync(string interactionId, DiscordModal modal, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>回答自动补全，最多 25 个选项。</summary>
    Task AutocompleteAsync(string interactionId, IReadOnlyList<DiscordCommandChoice> choices,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>编辑首次回复（包括自动确认产生的延迟回复）。</summary>
    Task<DiscordSentMessage> EditOriginalAsync(string interactionId, DiscordMessageContent content,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>删除首次回复。</summary>
    Task DeleteOriginalAsync(string interactionId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>追加一条消息；首次回复之后的 15 分钟内可用。</summary>
    Task<DiscordSentMessage> FollowupAsync(string interactionId, DiscordMessageContent content,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>已注册的全局命令，或 <paramref name="guildId"/> 服务器的命令。</summary>
    Task<IReadOnlyList<DiscordRegisteredCommand>> GetCommandsAsync(string? guildId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>用给定列表整体替换已注册的命令（不在列表中的命令会被删除）。全局命令可能需要几分钟同步到客户端。</summary>
    Task<IReadOnlyList<DiscordRegisteredCommand>> RegisterCommandsAsync(IReadOnlyList<DiscordApplicationCommand> commands,
        string? guildId = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>新增或更新同名命令，不影响其他命令。</summary>
    Task<DiscordRegisteredCommand> UpsertCommandAsync(DiscordApplicationCommand command, string? guildId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>删除一个已注册的命令。</summary>
    Task DeleteCommandAsync(string commandId, string? guildId = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

public enum DiscordStatus
{
    Online,
    Idle,

    /// <summary>请勿打扰。</summary>
    DoNotDisturb,
    Invisible
}

public enum DiscordActivityType
{
    Playing = 0,

    /// <summary>需要 Twitch 或 YouTube 的 <see cref="DiscordActivity.Url"/>。</summary>
    Streaming = 1,
    Listening = 2,
    Watching = 3,

    /// <summary>自定义状态，文本在 <see cref="DiscordActivity.State"/>。</summary>
    Custom = 4,
    Competing = 5
}

public sealed record DiscordActivity(DiscordActivityType Type, string Name)
{
    public string? State { get; init; }
    public string? Url { get; init; }
}

/// <summary>账号的在线状态与活动。</summary>
public sealed record DiscordPresence
{
    public DiscordStatus Status { get; init; } = DiscordStatus.Online;
    public DiscordActivity? Activity { get; init; }
    public bool Afk { get; init; }
}

/// <summary>
///     用户账号特有的操作，仅用户账号（机器人适配器不实现）。好友、历史消息、成员列表和退出服务器（<c>LeaveChannelAsync</c>）使用 SDK 通用服务。
///     Discord 服务条款禁止自动化用户账号，账号可能被封禁。
/// </summary>
public interface IDiscordAccountApi
{
    /// <summary>当前账号的私聊和群组私聊。</summary>
    Task<IReadOnlyList<DiscordChannel>> GetPrivateChannelsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>通过邀请码加入服务器。</summary>
    Task<DiscordGuild> JoinGuildAsync(string inviteCode, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>把频道标记为已读；<paramref name="maxMessageId"/> 为 null 时读到最新消息。</summary>
    Task MarkAsReadAsync(Channel channel, string? maxMessageId = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>随原始请求上传的文件，按顺序作为 <c>files[n]</c> 发送。</summary>
/// <param name="Content">请求发送时读取一次，调用方负责释放。</param>
public sealed record DiscordUploadFile(string FileName, Stream Content);

/// <summary>
///     直接调用任意 Discord REST 路由、发送 Gateway 指令并接收原始 Dispatch，覆盖本模型没有建模的功能。
///     只使用 System.Text.Json 类型，插件无需引用适配器程序集。
/// </summary>
public interface IDiscordRawApi
{
    /// <summary>Discord API 版本，如 10。</summary>
    int ApiVersion => 0;

    /// <summary>
    ///     调用 REST 路由，例如 <c>CallAsync("GET", "guilds/123/audit-logs")</c>。返回响应 JSON；204 时为 Undefined。
    ///     有 <paramref name="files"/> 时以 multipart 发送，<paramref name="body"/> 作为 payload_json。
    ///     失败抛出 <see cref="HttpRequestException"/>，消息包含状态码和 Discord 错误内容。
    /// </summary>
    Task<JsonElement> CallAsync(string method, string path, JsonNode? body = null,
        IReadOnlyList<DiscordUploadFile>? files = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>发送 Gateway 指令，例如 op 8 请求成员、op 4 更新语音状态。Identify、Resume 和心跳由适配器管理。</summary>
    Task SendGatewayAsync(int op, JsonNode? data, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>每个 Gateway Dispatch：事件名（如 MESSAGE_CREATE）与原始 data，先于 SDK 事件触发。</summary>
    event Func<string, JsonElement, Task>? DispatchReceived
    {
        add => throw new NotSupportedException();
        remove { }
    }
}
