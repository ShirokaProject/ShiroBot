using ShiroBot.Model.File.Requests;
using ShiroBot.Model.File.Responses;
using ShiroBot.SDK.Adapter;

namespace ShiroBot.SDK.Plugin;

public interface IFileContext : IFileService
{
    Task<UploadPrivateFileResponse> UploadPrivateFileAsync(long userId, string fileUri, string fileName) =>
        UploadPrivateFileAsync(new UploadPrivateFileRequest(userId, fileUri, fileName));

    Task<UploadGroupFileResponse> UploadGroupFileAsync(long groupId, string fileUri, string fileName, string parentFolderId = "/") =>
        UploadGroupFileAsync(new UploadGroupFileRequest(groupId, fileUri, fileName, parentFolderId));

    Task<GetPrivateFileDownloadUrlResponse> GetPrivateFileDownloadUrlAsync(long userId, string fileId, string fileHash) =>
        GetPrivateFileDownloadUrlAsync(new GetPrivateFileDownloadUrlRequest(userId, fileId, fileHash));

    Task<GetGroupFileDownloadUrlResponse> GetGroupFileDownloadUrlAsync(long groupId, string fileId) =>
        GetGroupFileDownloadUrlAsync(new GetGroupFileDownloadUrlRequest(groupId, fileId));

    Task<GetGroupFilesResponse> GetGroupFilesAsync(long groupId, string parentFolderId = "/") =>
        GetGroupFilesAsync(new GetGroupFilesRequest(groupId, parentFolderId));

    Task MoveGroupFileAsync(long groupId, string fileId, string targetFolderId, string parentFolderId = "/") =>
        MoveGroupFileAsync(new MoveGroupFileRequest(groupId, fileId, targetFolderId, parentFolderId));

    Task RenameGroupFileAsync(long groupId, string fileId, string newFileName, string parentFolderId = "/") =>
        RenameGroupFileAsync(new RenameGroupFileRequest(groupId, fileId, newFileName, parentFolderId));

    Task DeleteGroupFileAsync(long groupId, string fileId) =>
        DeleteGroupFileAsync(new DeleteGroupFileRequest(groupId, fileId));

    Task<CreateGroupFolderResponse> CreateGroupFolderAsync(long groupId, string folderName) =>
        CreateGroupFolderAsync(new CreateGroupFolderRequest(groupId, folderName));

    Task RenameGroupFolderAsync(long groupId, string folderId, string newFolderName) =>
        RenameGroupFolderAsync(new RenameGroupFolderRequest(groupId, folderId, newFolderName));

    Task DeleteGroupFolderAsync(long groupId, string folderId) =>
        DeleteGroupFolderAsync(new DeleteGroupFolderRequest(groupId, folderId));
    
    Task PersistGroupFileAsync(long groupId,string fileId) =>
        PersistGroupFileAsync(new PersistGroupFileRequest(groupId, fileId));
}
