namespace ShiroBot.SDK.Plugin;

public interface IWebHostContext
{
    bool IsEnabled { get; }

    string RegisterFile(
        string ownerId,
        string routePrefix,
        string filePath,
        TimeSpan? expiresAfter = null,
        string? contentType = null);

    void UnregisterOwner(string ownerId);
}
