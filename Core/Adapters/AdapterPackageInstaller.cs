using ShiroBot.Components.Reloading;
using ShiroBot.Components.Updates;
using ShiroBot.Hosting.Runtime;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Adapters;

/// <summary>Result of installing a prepared adapter package; <see cref="Result"/> is null when the update was staged for restart.</summary>
internal sealed record AdapterPackageInstallOutcome(AdapterInstallResult? Result, string? PendingReason)
{
    public bool PendingRestart => Result is null;
}

/// <summary>Installs or replaces an adapter package, shared by Dashboard installs and automatic updates.</summary>
internal static class AdapterPackageInstaller
{
    private const long MaxAdapterPackageBytes = 100L * 1024L * 1024L;

    /// <summary>
    /// Downloads an adapter package and installs or replaces it, shared by the console's market install and automatic
    /// updates. Running instances of the package restart on the new version; a new package gets no instance.
    /// </summary>
    /// <param name="sha256">Hex digest the download must match, as published by the adapter market.</param>
    /// <param name="expectedId">When set, a package for any other adapter is refused.</param>
    public static async Task<(AdapterPackageProbe Probe, AdapterPackageInstallOutcome Outcome)> InstallFromAssetAsync(
        AdapterPackageManager adapterPackages,
        AdapterManager adapterManager,
        ComponentReloadCoordinator reloadCoordinator,
        HostRuntimeState runtimeState,
        string assetUrl,
        string assetName,
        string? sha256,
        string? expectedId,
        bool enable,
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot", "adapter_updates", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, Path.GetFileName(assetName));
            await Update.Updater.DownloadFileAsync(assetUrl, path, cancellationToken, MaxAdapterPackageBytes, sha256).ConfigureAwait(false);
            var probe = adapterPackages.Prepare(path, root);
            if (expectedId is not null && !string.Equals(probe.Id, expectedId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"更新包的适配器 ID {probe.Id} 与 {expectedId} 不一致。");
            var outcome = await InstallAsync(adapterPackages, adapterManager, reloadCoordinator, runtimeState, probe, enable).ConfigureAwait(false);
            return (probe, outcome);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static async Task<AdapterPackageInstallOutcome> InstallAsync(
        AdapterPackageManager adapterPackages,
        AdapterManager adapterManager,
        ComponentReloadCoordinator reloadCoordinator,
        HostRuntimeState runtimeState,
        AdapterPackageProbe probe,
        bool enable)
    {
        var existing = adapterPackages.Get(probe.Id);
        var runningInstances = adapterPackages.ListInstances().Where(instance =>
            string.Equals(instance.PackageId, probe.Id, StringComparison.OrdinalIgnoreCase) &&
            adapterManager.LoadedIds.Contains(instance.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var wasLoaded = runningInstances.Length > 0;
        async Task ActivateInstances(InstalledAdapterPackage package, bool restoring = false)
        {
            var candidates = adapterPackages.ListInstances().Where(instance => string.Equals(instance.PackageId, package.Id, StringComparison.OrdinalIgnoreCase) &&
                (instance.Active || runningInstances.Any(previous => string.Equals(previous.Id, instance.Id, StringComparison.OrdinalIgnoreCase)))).ToArray();
            var started = new List<string>();
            try
            {
                foreach (var instance in candidates)
                {
                    await adapterManager.LoadInstanceAsync(instance, forceFreshImage: restoring).ConfigureAwait(false);
                    started.Add(instance.Id);
                }
            }
            catch
            {
                foreach (var id in started)
                    try { await adapterManager.StopByIdAsync(id).ConfigureAwait(false); } catch (ComponentUnloadPendingException) { }
                throw;
            }
        }
        string? pendingReason = null;
        var result = await reloadCoordinator.ExecuteAdapterMutationAsync(async () =>
        {
            try
            {
                foreach (var instance in runningInstances) await adapterManager.StopByIdAsync(instance.Id).ConfigureAwait(false);
                return await adapterPackages.InstallAndActivateAsync(
                    probe,
                    existing?.Enabled ?? enable,
                    installed => ActivateInstances(installed),
                    wasLoaded ? restored => ActivateInstances(restored, restoring: true) : null,
                    activateWhenDisabled: wasLoaded).ConfigureAwait(false);
            }
            catch (Exception ex) when (existing is not null &&
                                       ex is ComponentUnloadPendingException or IOException or UnauthorizedAccessException)
            {
                // Stage the new version for restart. Never restart an instance whose stopped
                // assembly remains referenced; unaffected instances can keep serving.
                pendingReason = ex.Message;
                adapterPackages.StageUpdate(probe, existing.Enabled);
                foreach (var previous in runningInstances)
                {
                    if (adapterManager.LoadedIds.Contains(previous.Id, StringComparer.OrdinalIgnoreCase)) continue;
                    if (adapterManager.GetSnapshot().Any(item => item.Id == previous.Id && item.RestartRequired)) continue;
                    try { await adapterManager.LoadInstanceAsync(previous).ConfigureAwait(false); }
                    catch (Exception reloadError) { BotLog.Warning($"重新启动当前版本 Adapter {previous.Id} 失败: {reloadError.Message}"); }
                }
                return null;
            }
        }).ConfigureAwait(false);
        if (result is null)
        {
            runtimeState.RecommendRestart("adapter-update:" + probe.Id,
                $"适配器 {probe.Id} 的新版本 {probe.Version} 已暂存，需要重启宿主完成更新。");
        }
        return new AdapterPackageInstallOutcome(result, pendingReason);
    }
}
