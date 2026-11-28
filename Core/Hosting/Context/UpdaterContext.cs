using ShiroBot.SDK.Plugin;
using CoreUpdater = ShiroBot.Update.Updater;

namespace ShiroBot.Hosting.Context;

internal sealed class UpdaterContext : IUpdater
{
    public Task<GitHubReleaseUpdate?> CheckGitHubReleaseAsync(
        string repository,
        string currentVersion,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        return CoreUpdater.CheckGitHubReleaseAsync(repository, currentVersion, includePrerelease, cancellationToken);
    }

    public Task<string> RequestHostUpdateAsync(
        GitHubReleaseUpdate update,
        CancellationToken cancellationToken = default)
    {
        return CoreUpdater.RequestHostUpdateAsync(update, cancellationToken);
    }

    public Task<string> RequestPluginUpdateAsync(
        PluginUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return CoreUpdater.RequestPluginUpdateAsync(request, cancellationToken);
    }

    public IReadOnlyList<PendingUpdateInfo> GetPendingUpdates()
    {
        return CoreUpdater.GetPendingUpdates();
    }

    public Task<bool> ConfirmUpdateAsync(string requestId, CancellationToken cancellationToken = default)
    {
        return CoreUpdater.ConfirmUpdateAsync(requestId, cancellationToken);
    }

    public bool CancelUpdate(string requestId)
    {
        return CoreUpdater.CancelUpdate(requestId);
    }
}