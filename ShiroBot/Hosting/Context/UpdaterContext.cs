using ShiroBot.SDK.Plugin;
using CoreUpdater = ShiroBot.Core.Updater;

namespace ShiroBot.Hosting.Context;

internal sealed class UpdaterContext : IUpdater
{
    public Task<GitHubReleaseUpdate?> CheckGitHubReleaseAsync(
        string repository,
        string currentVersion,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default) =>
        CoreUpdater.CheckGitHubReleaseAsync(repository, currentVersion, includePrerelease, cancellationToken);

    public Task<string> RequestHostUpdateAsync(
        GitHubReleaseUpdate update,
        CancellationToken cancellationToken = default) =>
        CoreUpdater.RequestHostUpdateAsync(update, cancellationToken);

    public Task<string> RequestPluginUpdateAsync(
        PluginUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        CoreUpdater.RequestPluginUpdateAsync(request, cancellationToken);

    public IReadOnlyList<PendingUpdateInfo> GetPendingUpdates() =>
        CoreUpdater.GetPendingUpdates();

    public Task<bool> ConfirmUpdateAsync(string requestId, CancellationToken cancellationToken = default) =>
        CoreUpdater.ConfirmUpdateAsync(requestId, cancellationToken);

    public bool CancelUpdate(string requestId) =>
        CoreUpdater.CancelUpdate(requestId);
}
