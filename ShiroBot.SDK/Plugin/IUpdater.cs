namespace ShiroBot.SDK.Plugin;

public enum UpdateTarget
{
    Host,
    Plugin
}

public sealed record GitHubReleaseUpdate(
    string Repository,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseName,
    string? ReleaseUrl,
    string? ReleaseNotes);

public sealed record PluginUpdateRequest(
    string PluginName,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseUrl = null,
    string? ReleaseNotes = null);

public sealed record PendingUpdateInfo(
    string Id,
    UpdateTarget Target,
    string Name,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseUrl);

public interface IUpdater
{
    Task<GitHubReleaseUpdate?> CheckGitHubReleaseAsync(
        string repository,
        string currentVersion,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    Task<string> RequestHostUpdateAsync(
        GitHubReleaseUpdate update,
        CancellationToken cancellationToken = default);

    Task<string> RequestPluginUpdateAsync(
        PluginUpdateRequest request,
        CancellationToken cancellationToken = default);

    IReadOnlyList<PendingUpdateInfo> GetPendingUpdates();

    Task<bool> ConfirmUpdateAsync(string requestId, CancellationToken cancellationToken = default);

    bool CancelUpdate(string requestId);
}
