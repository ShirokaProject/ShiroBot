namespace ShiroBot.SDK.Models;

public sealed record FileCapabilities
{
    public bool CanUpload { get; init; }
    /// <summary>上传是否同时发布给会话接收者。false 表示仅取得平台上传凭据。</summary>
    public bool UploadPublishes { get; init; }
}

public sealed record FileUploadRequest
{
    /// <summary>HTTP(S)、file URI、本地绝对路径或 base64 URI；具体支持情况由适配器决定。</summary>
    public required string Uri { get; init; }
    public required string FileName { get; init; }
}

public sealed record FileUploadResult
{
    /// <summary>当前实例及目标会话范围内的不透明文件 ID 或媒体凭据；不是消息 ID。</summary>
    public required string FileId { get; init; }
    public required bool IsPublished { get; init; }
}
