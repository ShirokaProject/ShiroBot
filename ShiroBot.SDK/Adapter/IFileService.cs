using ShiroBot.Model.File.Requests;
using ShiroBot.Model.File.Responses;

namespace ShiroBot.SDK.Adapter;

public interface IFileService
{
    Task<UploadPrivateFileResponse> UploadPrivateFileAsync(UploadPrivateFileRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(UploadPrivateFileAsync)}'.");

    Task<UploadGroupFileResponse> UploadGroupFileAsync(UploadGroupFileRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(UploadGroupFileAsync)}'.");

    Task<GetPrivateFileDownloadUrlResponse> GetPrivateFileDownloadUrlAsync(GetPrivateFileDownloadUrlRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetPrivateFileDownloadUrlAsync)}'.");

    Task<GetGroupFileDownloadUrlResponse> GetGroupFileDownloadUrlAsync(GetGroupFileDownloadUrlRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetGroupFileDownloadUrlAsync)}'.");

    Task<GetGroupFilesResponse> GetGroupFilesAsync(GetGroupFilesRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetGroupFilesAsync)}'.");

    Task MoveGroupFileAsync(MoveGroupFileRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(MoveGroupFileAsync)}'.");

    Task RenameGroupFileAsync(RenameGroupFileRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(RenameGroupFileAsync)}'.");

    Task DeleteGroupFileAsync(DeleteGroupFileRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(DeleteGroupFileAsync)}'.");

    Task<CreateGroupFolderResponse> CreateGroupFolderAsync(CreateGroupFolderRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(CreateGroupFolderAsync)}'.");

    Task RenameGroupFolderAsync(RenameGroupFolderRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(RenameGroupFolderAsync)}'.");

    Task DeleteGroupFolderAsync(DeleteGroupFolderRequest request)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(DeleteGroupFolderAsync)}'.");
    
    Task PersistGroupFileAsync(PersistGroupFileRequest request) 
        => throw new NotSupportedException($"Current adapter does not support '{nameof(PersistGroupFileRequest)}'.");
}
