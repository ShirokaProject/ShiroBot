using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Discord;

// 插件通过 Context.GetAdapterExtension<T>() 探测这些接口。ID 参数使用 Snowflake 字符串，与 SDK 的 User.Id、Channel.Id、
// MessageId 相同；消息方法的 Channel 为 Direct 时 Id 为用户 ID，适配器自动打开私聊。
// reason 写入服务器审计日志。未实现的方法默认抛 NotSupportedException；权限由 Discord 校验。

/// <summary>服务器、频道、角色、成员、封禁、邀请、表情和子区管理。</summary>
public interface IDiscordGuildApi
{
    Task<DiscordGuild> GetGuildAsync(string guildId, bool withCounts = false, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>机器人所在的全部服务器。</summary>
    Task<IReadOnlyList<DiscordGuild>> GetGuildsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<IReadOnlyList<DiscordChannel>> GetGuildChannelsAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<DiscordChannel> CreateChannelAsync(string guildId, DiscordChannelOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<DiscordChannel> ModifyChannelAsync(string channelId, DiscordChannelOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

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

    Task DeleteChannelPermissionAsync(string channelId, string roleOrUserId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<IReadOnlyList<DiscordRole>> GetRolesAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<DiscordRole> CreateRoleAsync(string guildId, DiscordRoleOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<DiscordRole> ModifyRoleAsync(string guildId, string roleId, DiscordRoleOptions options, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task DeleteRoleAsync(string guildId, string roleId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

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

    Task AddMemberRoleAsync(string guildId, string userId, string roleId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

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

    Task UnbanAsync(string guildId, string userId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<IReadOnlyList<DiscordBan>> GetBansAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<IReadOnlyList<DiscordInvite>> GetInvitesAsync(string guildId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<DiscordInvite> CreateInviteAsync(string channelId, DiscordInviteOptions? options = null, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task DeleteInviteAsync(string code, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

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

    Task JoinThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task LeaveThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task AddThreadMemberAsync(string threadId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Discord 原生消息：Embed、组件、投票、附件、编辑、置顶、批量删除和 Reaction 管理。</summary>
public interface IDiscordMessageApi
{
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

    Task PinAsync(Channel channel, string messageId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task UnpinAsync(Channel channel, string messageId, string? reason = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

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

    Task<IReadOnlyList<DiscordUser>> GetPollVotersAsync(Channel channel, string messageId, int answerId, int limit = 25,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
///     交互回复与应用命令。交互令牌有效期 15 分钟，首次回复必须在 3 秒内发出。
///     适配器自动确认交互时（<see cref="AutoAcknowledges"/>），首次回复已用延迟回复占用，只能使用
///     <see cref="EditOriginalAsync"/> 和 <see cref="FollowupAsync"/>；弹窗、自动补全和临时可见的首次回复需要关闭自动确认。
/// </summary>
public interface IDiscordInteractionApi
{
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

    Task<DiscordSentMessage> EditOriginalAsync(string interactionId, DiscordMessageContent content,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

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

/// <summary>机器人的在线状态与活动。</summary>
public sealed record DiscordPresence
{
    public DiscordStatus Status { get; init; } = DiscordStatus.Online;
    public DiscordActivity? Activity { get; init; }
    public bool Afk { get; init; }
}

/// <summary>机器人自身：资料、在线状态和用户查询。</summary>
public interface IDiscordBotApi
{
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

    /// <summary>修改机器人用户名或头像；null 表示不修改。Discord 对用户名修改有频率限制。</summary>
    Task<DiscordUser> ModifyCurrentUserAsync(string? username = null, string? avatarUri = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>应用 ID；READY 之后可用。</summary>
    string? ApplicationId => null;
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
