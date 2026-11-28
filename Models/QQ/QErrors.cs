namespace ShiroBot.Model.QQ;

/// <summary>QQ 平台拒绝了某个操作。适配器负责识别错误码，插件只按类型处理结果。</summary>
public abstract class QPlatformException(string message, string operation, int? errorCode, string? detail)
    : Exception(message)
{
    /// <summary>被拒绝的操作，例如“群黑名单查询”。</summary>
    public string Operation { get; } = operation;

    public int? ErrorCode { get; } = errorCode;

    public string? Detail { get; } = detail;

    private protected static string Code(int? errorCode) => errorCode?.ToString() ?? "unknown";
}

/// <summary>平台未给当前机器人开通某个 QQ 接口权限。</summary>
public sealed class QPermissionDeniedException(string operation, int? errorCode, string? detail = null)
    : QPlatformException($"QQ 平台未授权接口：{operation}（错误码 {Code(errorCode)}）", operation, errorCode, detail);

/// <summary>QQ 平台限制了操作次数，例如每群每天一次的 @全体成员。</summary>
public sealed class QLimitExceededException(string operation, int? errorCode, string? detail = null)
    : QPlatformException($"QQ 平台操作次数已用完：{operation}（错误码 {Code(errorCode)}）", operation, errorCode, detail);
