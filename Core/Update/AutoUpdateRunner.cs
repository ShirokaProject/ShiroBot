using System.Text.Json.Nodes;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Plugins;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Update;

/// <summary>
/// One automatic update pass: plugins, then adapters, then the host. A host update restarts the process
/// itself; otherwise updates that could only be staged restart the host when
/// <see cref="CoreConfig.AutoUpdateRestartOnPending"/> is on.
/// </summary>
internal sealed class AutoUpdateRunner(
    CoreConfig config,
    PluginManager pluginManager,
    HostEventDispatcher eventDispatcher,
    PluginRouteConfig routePolicy,
    AdapterPackageManager adapterPackages,
    AdapterManager adapterManager,
    ComponentReloadCoordinator reloadCoordinator,
    HostRuntimeState runtimeState,
    HostPowerControl powerControl)
{
    private const long MaxAdapterPackageBytes = 100L * 1024L * 1024L;
    private readonly AdapterMarketplaceCache _adapterMarketplace = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pending = new List<string>();
        await UpdatePluginsAsync(pending, cancellationToken).ConfigureAwait(false);
        await UpdateAdaptersAsync(pending, cancellationToken).ConfigureAwait(false);
        if (await UpdateHostAsync(cancellationToken).ConfigureAwait(false)) return;

        if (pending.Count == 0)
        {
            BotLog.Info("自动更新完成。");
            return;
        }

        var names = string.Join("、", pending);
        if (!config.AutoUpdateRestartOnPending)
        {
            BotLog.Warning($"自动更新完成，{names} 无法热替换，新版本已暂存，需要重启宿主完成更新。");
            return;
        }

        BotLog.Warning($"自动更新完成，{names} 无法热替换，正在重启宿主完成更新...");
        var restart = powerControl.Restart();
        if (!restart.Ok) BotLog.Error("自动重启失败，请手动重启宿主: " + restart.Message);
    }

    private async Task UpdatePluginsAsync(List<string> pending, CancellationToken cancellationToken)
    {
        foreach (var plugin in pluginManager.GetLoadedPluginSnapshot().Where(plugin => !string.IsNullOrWhiteSpace(plugin.GithubRepo)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var update = await Updater.CheckGitHubPluginPackageUpdateAsync(plugin.GithubRepo!, plugin.Version,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (update is null) continue;
                if (string.IsNullOrWhiteSpace(update.AssetDownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName))
                {
                    BotLog.Warning($"插件 {plugin.Name} 有新版本 {update.LatestVersion}，但 release 中没有可用的 zip 或 dll 插件包，已跳过。");
                    continue;
                }

                var result = await PluginUpdateService.ApplyAsync(pluginManager, eventDispatcher, routePolicy,
                    plugin.Name, update.AssetDownloadUrl, update.AssetName, update.LatestVersion, cancellationToken).ConfigureAwait(false);
                BotLog.Info(result.Message);
                if (!result.PendingRestart) continue;
                runtimeState.RecommendRestart("plugin-update:" + plugin.Name, result.Message);
                pending.Add("插件 " + plugin.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BotLog.Warning($"插件 {plugin.Name} 自动更新失败: {ex.Message}");
            }
        }
    }

    private async Task UpdateAdaptersAsync(List<string> pending, CancellationToken cancellationToken)
    {
        var installed = adapterPackages.List();
        if (installed.Count == 0) return;
        var entries = await _adapterMarketplace.GetAsync(cancellationToken).ConfigureAwait(false);
        foreach (var package in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var release = entries.OfType<JsonObject>().FirstOrDefault(entry =>
                string.Equals(entry["id"]?.GetValue<string>(), package.Id, StringComparison.OrdinalIgnoreCase))?["release"] as JsonObject;
            var version = release?["version"]?.GetValue<string>();
            if (release is null || string.IsNullOrWhiteSpace(version) || release["prerelease"]?.GetValue<bool>() == true ||
                !Updater.IsNewerVersion(version, package.Version))
                continue;
            if (release["asset"] is not JsonObject asset ||
                asset["url"]?.GetValue<string>() is not { Length: > 0 } assetUrl ||
                asset["name"]?.GetValue<string>() is not { Length: > 0 } assetName)
            {
                BotLog.Warning($"适配器 {package.Id} 有新版本 {version}，但市场中没有可下载的包，已跳过。");
                continue;
            }

            var digest = asset["digest"]?.GetValue<string>() is { } value && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? value["sha256:".Length..]
                : null;
            var root = Path.Combine(Path.GetTempPath(), "ShiroBot", "adapter_updates", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, Path.GetFileName(assetName));
                await Updater.DownloadFileAsync(assetUrl, path, cancellationToken, MaxAdapterPackageBytes, digest).ConfigureAwait(false);
                var probe = adapterPackages.Prepare(path, root);
                if (!string.Equals(probe.Id, package.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"更新包的适配器 ID {probe.Id} 与 {package.Id} 不一致。");
                var outcome = await AdapterPackageInstaller.InstallAsync(adapterPackages, adapterManager, reloadCoordinator, runtimeState,
                    probe, package.Enabled).ConfigureAwait(false);
                if (outcome.PendingRestart)
                {
                    BotLog.Warning($"适配器 {package.Id} 当前版本无法热替换，{probe.Version} 已暂存，将在重启宿主时替换。");
                    pending.Add("适配器 " + package.Id);
                }
                else if (outcome.Result!.StartError is { } startError)
                    BotLog.Warning($"适配器 {package.Id} 已更新到 {probe.Version}，但启动失败: {startError}");
                else
                    BotLog.Info($"适配器 {package.Id} 已更新到 {probe.Version}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BotLog.Warning($"适配器 {package.Id} 自动更新失败: {ex.Message}");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Returns true when the host is restarting into a new version.</summary>
    private async Task<bool> UpdateHostAsync(CancellationToken cancellationToken)
    {
        try
        {
            var check = await HostSelfUpdater.CheckAsync(config.HostUpdateRepository, cancellationToken).ConfigureAwait(false);
            if (!check.UpdateAvailable) return false;
            if (!check.CanApply)
            {
                BotLog.Warning($"主程序有新版本 {check.LatestVersion}，但无法自动更新: {check.Reason}");
                return false;
            }

            var result = await HostSelfUpdater.ApplyAsync(check.AssetDownloadUrl!, powerControl, cancellationToken).ConfigureAwait(false);
            if (result.Ok)
            {
                BotLog.Info($"主程序已更新到 {check.LatestVersion}: {result.Message}");
                return true;
            }

            BotLog.Warning("主程序自动更新失败: " + result.Message);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BotLog.Warning("主程序自动更新失败: " + ex.Message);
            return false;
        }
    }
}
