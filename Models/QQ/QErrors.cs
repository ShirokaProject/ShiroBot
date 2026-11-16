namespace ShiroBot.Model.QQ;

/// <summary>平台未给当前机器人开通某个 QQ 接口权限。适配器负责识别错误码，插件只消费结果。</summary>
public sealed class QPermissionDeniedException(string operation, int? errorCode, string? detail = null)
    : Exception($"QQ 平台未授权接口：{operation}（错误码 {errorCode?.ToString() ?? "unknown"}）")
{
    /// <summary>被拒绝的操作，例如“群黑名单查询”。</summary>
    public string Operation { get; } = operation;

    public int? ErrorCode { get; } = errorCode;

    public string? Detail { get; } = detail;
}

/// <summary>QQ 平台限制了操作次数，例如每群每天一次的 @全体成员。</summary>
public sealed class QLimitExceededException(string operation, int? errorCode, string? detail = null)
    : Exception($"QQ 平台操作次数已用完：{operation}（错误码 {errorCode?.ToString() ?? "unknown"}）")
{
    public string Operation { get; } = operation;

    public int? ErrorCode { get; } = errorCode;

    public string? Detail { get; } = detail;
}
