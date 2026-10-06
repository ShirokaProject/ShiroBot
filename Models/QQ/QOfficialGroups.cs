namespace ShiroBot.Model.QQ;

/// <summary>QQ 官方群管理扩展；ID 使用 OpenID。管理操作需要群管理员及平台接口权限。</summary>
public interface IQOfficialGroupApi
{
    Task<QOfficialJoinRequestPage> GetJoinRequestsAsync(string groupOpenId, string? cursor = null,
        int limit = 20, CancellationToken cancellationToken = default);
    Task ApproveJoinRequestAsync(string groupOpenId, string memberOpenId, string joinRequestId,
        CancellationToken cancellationToken = default);
    Task RejectJoinRequestAsync(string groupOpenId, string memberOpenId, string joinRequestId,
        string? reason = null, bool addToBlacklist = false, CancellationToken cancellationToken = default);
    Task<QOfficialGroupMuteState> GetMuteStateAsync(string groupOpenId, CancellationToken cancellationToken = default);
    /// <summary>单次最多 20 个成员；Duration 为零解除禁言，最长 30 天。</summary>
    Task SetMemberMutesAsync(string groupOpenId, IReadOnlyList<QOfficialMemberMute> members,
        CancellationToken cancellationToken = default);
    Task<QOfficialApprovalStrategyPage> GetApprovalStrategiesAsync(string? cursor = null, int limit = 20,
        CancellationToken cancellationToken = default);
    Task<QOfficialApprovalStrategy> CreateApprovalStrategyAsync(QOfficialApprovalStrategyOptions options,
        CancellationToken cancellationToken = default);
    Task<QOfficialApprovalStrategy> UpdateApprovalStrategyAsync(string strategyId, QOfficialApprovalStrategyUpdate update,
        CancellationToken cancellationToken = default);
    Task DeleteApprovalStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    /// <summary>触发平台异步扫描，接口返回不代表扫描已完成。</summary>
    Task ExecuteApprovalStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    /// <summary>单次最多 10000 个 QQ 号码。返回平台估算的白名单总数。</summary>
    Task<int> UpdateApprovalWhitelistAsync(string strategyId, IReadOnlyList<string> qqNumbers, bool add = true,
        CancellationToken cancellationToken = default);
}

public sealed record QOfficialGroupMemberEvent : QEventPayload
{
    public required string GroupOpenId { get; init; }
    public required string MemberOpenId { get; init; }
    public string? UserOpenId { get; init; }
    public string? EventId { get; init; }
}

public sealed record QOfficialJoinRequest : QEventPayload
{
    public required string GroupOpenId { get; init; }
    public required string MemberOpenId { get; init; }
    public required string JoinRequestId { get; init; }
    public string? EventId { get; init; }
    public string? Username { get; init; }
    public string? UnionOpenId { get; init; }
    public string? RiskTips { get; init; }
    public DateTimeOffset? ApplyAt { get; init; }
    public string? ApplySource { get; init; }
    public string? InvitedBy { get; init; }
    public bool IsBot { get; init; }
    public QOfficialJoinVerification? Verification { get; init; }
    public string? AutoApprovedStrategyId { get; init; }
}

public sealed record QOfficialJoinVerification(string? Method, string? Message, IReadOnlyList<QOfficialReviewAnswer> Answers);
public sealed record QOfficialReviewAnswer(string? Question, string? Answer);
public sealed record QOfficialJoinRequestPage(IReadOnlyList<QOfficialJoinRequest> Requests, string? NextCursor);
public sealed record QOfficialMemberMute(string MemberOpenId, TimeSpan Duration, bool UpdateExisting = false);
public sealed record QOfficialMutedMember(string MemberOpenId, DateTimeOffset? ExpiresAt, string? Username, string? UnionOpenId);
public sealed record QOfficialMuteSchedule(string TaskId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool Enabled);
/// <summary>周期时段使用北京时间；结束时间早于开始时间代表跨天。</summary>
public sealed record QOfficialMuteRecurring(string TaskId, IReadOnlyList<int> Weekdays, string? StartTime, string? EndTime, bool Enabled);
public sealed record QOfficialGroupMuteState(string? Mode, IReadOnlyList<QOfficialMuteSchedule> Schedules,
    IReadOnlyList<QOfficialMuteRecurring> RecurringRules, IReadOnlyList<QOfficialMutedMember> Members);

public sealed record QOfficialApprovalStrategyOptions
{
    /// <summary>与 GroupIds 二选一，最多 100 个。</summary>
    public IReadOnlyList<string>? GroupOpenIds { get; init; }
    public IReadOnlyList<ulong>? GroupIds { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Remark { get; init; }
}

public sealed record QOfficialApprovalStrategyUpdate
{
    public bool? Enabled { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Remark { get; init; }
    public QOfficialApprovalGroupAction? Groups { get; init; }
}

public sealed record QOfficialApprovalGroupAction
{
    public bool Add { get; init; } = true;
    public IReadOnlyList<string>? GroupOpenIds { get; init; }
    public IReadOnlyList<ulong>? GroupIds { get; init; }
}

public sealed record QOfficialApprovalStrategy
{
    public required string StrategyId { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? Remark { get; init; }
    public IReadOnlyList<string> GroupOpenIds { get; init; } = [];
    /// <summary>平台查询响应可能脱敏，因此保留字符串。</summary>
    public IReadOnlyList<string> GroupIds { get; init; } = [];
    public int WhitelistUserCount { get; init; }
}
public sealed record QOfficialApprovalStrategyPage(IReadOnlyList<QOfficialApprovalStrategy> Strategies, string? NextCursor);
