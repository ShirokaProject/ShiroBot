namespace ShiroBot.Model.QQ;

/// <summary>Markdown 图片素材管理。与聊天媒体的 file_info 上传相互独立。</summary>
public interface IQOfficialMaterialApi
{
    /// <summary>上传不超过 10 MB 的 PNG/JPEG/GIF/WebP；从当前位置读取，不关闭调用方流。</summary>
    Task<QOfficialImageMaterial> UploadImageAsync(Stream content, string fileName,
        CancellationToken cancellationToken = default);

    Task<QOfficialImageMaterialPage> GetImagesAsync(string? cursor = null,
        CancellationToken cancellationToken = default);

    Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default);
}

/// <summary>Url 为 qqbot-media:// 引用；PreviewUrl 是短效地址，不应持久化。</summary>
public sealed record QOfficialImageMaterial(string ImageId, string Url)
{
    public string? FileName { get; init; }
    public string? PreviewUrl { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record QOfficialImageMaterialPage(
    IReadOnlyList<QOfficialImageMaterial> Images, string? NextCursor, int Total);
