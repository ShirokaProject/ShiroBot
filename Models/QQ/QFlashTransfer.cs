namespace ShiroBot.Model.QQ;

/// <summary>闪传文件集里的一个文件。</summary>
public sealed record QFlashFile(string Name, long Size)
{
    /// <summary>本地路径；下载完成后由适配器填写，其他时候为 null。</summary>
    public string? Path { get; init; }
}

/// <summary>闪传分享：上传或重新分享后得到的文件集 ID、分享链接和过期时间。</summary>
public sealed record QFlashShare(string FileSetId, string ShareLink, DateTimeOffset ExpiresAt);

/// <summary>闪传文件集详情。</summary>
public sealed record QFlashFileSet
{
    public required string FileSetId { get; init; }
    public string? Title { get; init; }
    public required string ShareLink { get; init; }

    /// <summary>文件总大小（字节）。</summary>
    public long TotalSize { get; init; }

    public IReadOnlyList<QFlashFile> Files { get; init; } = [];
}

/// <summary>闪传传输阶段。</summary>
public enum QFlashTransferState
{
    Uploading = 0,
    Uploaded = 1,
    Downloading = 2,
    Downloaded = 3
}
