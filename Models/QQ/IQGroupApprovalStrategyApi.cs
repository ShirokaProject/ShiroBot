namespace ShiroBot.Model.QQ;

/// <summary>可选的群入群自动审批策略扩展。与基本群管理分开，按能力探测。</summary>
public interface IQGroupApprovalStrategyApi
{
    Task<QApprovalStrategyPage> GetApprovalStrategiesAsync(string? cursor = null, int limit = 20, CancellationToken cancellationToken = default);
    Task<QApprovalStrategy> CreateApprovalStrategyAsync(QApprovalStrategyOptions options, CancellationToken cancellationToken = default);
    Task<QApprovalStrategy> UpdateApprovalStrategyAsync(string strategyId, QApprovalStrategyUpdate update, CancellationToken cancellationToken = default);
    Task DeleteApprovalStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    /// <summary>触发平台异步扫描；返回不代表已完成。</summary>
    Task ExecuteApprovalStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    Task<int> UpdateApprovalWhitelistAsync(string strategyId, IReadOnlyList<string> qqNumbers, bool add = true, CancellationToken cancellationToken = default);
}

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
