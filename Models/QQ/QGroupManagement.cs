namespace ShiroBot.Model.QQ;

public sealed record QJoinVerification(string? Method, string? Message, IReadOnlyList<QReviewAnswer> Answers);
public sealed record QReviewAnswer(string? Question, string? Answer);
public sealed record QGroupJoinRequestPage(IReadOnlyList<QGroupJoinRequest> Requests, string? NextCursor);
/// <summary>将成员禁言设置为指定时长；零表示解除。不暴露协议 add/update 模式。</summary>
public sealed record QMemberMute
{
    public required string UserId { get; init; }
    public required TimeSpan Duration { get; init; }
}

public enum QOperationStatus { Succeeded, Failed, Unknown, NotExecuted }
public sealed record QOperationResult
{
    public required string UserId { get; init; }
    public required QOperationStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}
/// <summary>按输入顺序返回；失败不会回滚已经完成的操作。</summary>
public sealed record QBatchOperationResult
{
    public IReadOnlyList<QOperationResult> Items { get; init; } = [];
    public bool IsSuccess => Items.All(x => x.Status == QOperationStatus.Succeeded);
}
/// <summary>取消仍抛取消异常；PartialResult 描述已执行、结果未知和未执行的项。</summary>
public sealed class QBatchOperationCanceledException(QBatchOperationResult partialResult, CancellationToken token)
    : OperationCanceledException("Batch canceled; inspect PartialResult before retrying.", token)
{
    public QBatchOperationResult PartialResult { get; } = partialResult;
}

/// <summary>default 为未指定；Set(value) 为设置；Clear() 为显式清空。</summary>
public readonly record struct QPatch<T>
{
    public bool IsSpecified { get; private init; }
    public bool IsClear { get; private init; }
    public T? Value { get; private init; }
    public static QPatch<T> Set(T value) => value is null ? throw new ArgumentNullException(nameof(value))
        : new() { IsSpecified = true, Value = value };
    public static QPatch<T> Clear() => new() { IsSpecified = true, IsClear = true };
}
public sealed record QMutedMember(string UserId, DateTimeOffset? ExpiresAt, string? Username, string? UnionId);
public sealed record QMuteSchedule(string TaskId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool Enabled);
/// <summary>周期时段使用北京时间；结束时间早于开始时间代表跨天。</summary>
public sealed record QMuteRecurring(string TaskId, IReadOnlyList<int> Weekdays, string? StartTime, string? EndTime, bool Enabled);
public sealed record QGroupMuteState(string? Mode, IReadOnlyList<QMuteSchedule> Schedules,
    IReadOnlyList<QMuteRecurring> RecurringRules, IReadOnlyList<QMutedMember> Members);

public sealed record QApprovalStrategyOptions
{
    /// <summary>与 GroupNumbers 二选一，最多 100 个。</summary>
    public IReadOnlyList<string>? GroupIds { get; init; }
    public IReadOnlyList<string>? GroupNumbers { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Remark { get; init; }
}

public sealed record QApprovalStrategyUpdate
{
    public bool? Enabled { get; init; }
    public QPatch<DateTimeOffset> ExpiresAt { get; init; }
    public QPatch<string> Remark { get; init; }
    public QApprovalGroupAction? Groups { get; init; }
}

public sealed record QApprovalGroupAction
{
    public bool Add { get; init; } = true;
    public IReadOnlyList<string>? GroupIds { get; init; }
    public IReadOnlyList<string>? GroupNumbers { get; init; }
}

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
public sealed record QApprovalStrategyPage(IReadOnlyList<QApprovalStrategy> Strategies, string? NextCursor);
