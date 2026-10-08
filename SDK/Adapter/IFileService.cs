using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

/// <summary>可选文件上传能力；从当前实例的 GetAdapterExtension 获取。文件 ID 不可跨实例或会话使用。</summary>
public interface IFileService
{
    FileCapabilities GetFileCapabilities(Channel channel);
    Task<FileUploadResult> UploadAsync(Channel channel, FileUploadRequest request,
        CancellationToken cancellationToken = default);
}
