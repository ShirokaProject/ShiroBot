using System.Text.Json.Nodes;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Plugins;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Update;

/// <summary>
/// One automatic update pass over the enabled kinds: plugins, then adapters, then the host. A host update restarts the process
/// itself; otherwise updates that could only be staged restart the host when
/// <see cref="CoreConfig.AutoUpdateRestartOnPending"/> is on. What happened is sent to the owners as one
/// summary, before any restart; a run with nothing to update sends nothing.
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
    HostPowerControl powerControl,
    OwnerNotify notifyOwners)
{
    private const long MaxAdapterPackageBytes = 100L * 1024L * 1024L;
    private readonly AdapterMarketplaceCache _adapterMarketplace = new();
    // Runs never overlap: the scheduler starts the next one only after this one returns.
    private readonly List<string> _report = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _report.Clear();
        try
        {
            var pending = new List<string>();
            if (config.AutoUpdatePlugins) await UpdatePluginsAsync(pending, cancellationToken).ConfigureAwait(false);
            if (config.AutoUpdateAdapters) await UpdateAdaptersAsync(pending, cancellationToken).ConfigureAwait(false);
            if (config.AutoUpdateHost && await UpdateHostAsync(cancellationToken).ConfigureAwait(false)) return;

            if (pending.Count == 0)
            {
                BotLog.Info("自动更新完成。");
                await SendReportAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var names = string.Join("、", pending);
            if (!config.AutoUpdateRestartOnPending)
            {
                Report($"{names} 无法热替换，新版本已暂存，需要重启宿主完成更新。", warning: true);
                await SendReportAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            Report($"{names} 无法热替换，正在重启宿主完成更新…", warning: true);
            await SendReportAsync(cancellationToken).ConfigureAwait(false);
            var restart = powerControl.Restart();
            if (restart.Ok) return;
            BotLog.Error("自动重启失败，请手动重启宿主: " + restart.Message);
            await notifyOwners("【ShiroBot 自动更新】自动重启失败，请手动重启宿主: " + restart.Message, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report("自动更新中断: " + ex.Message, warning: true);
            await SendReportAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private void Report(string message, bool warning = false)
    {
        if (warning) BotLog.Warning(message);
        else BotLog.Info(message);
        _report.Add(message);
    }

    /// <summary>Sends what has happened since the last report to the owners, if anything.</summary>
    private async Task SendReportAsync(CancellationToken cancellationToken)
    {
        if (_report.Count == 0) return;
        var text = "【ShiroBot 自动更新】\n" + string.Join("\n", _report.Select(line => "· " + line));
        _report.Clear();
        await notifyOwners(text, null, cancellationToken).ConfigureAwait(false);
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
                    Report($"插件 {plugin.Name} 有新版本 {update.LatestVersion}，但 release 中没有可用的 zip 或 dll 插件包，已跳过。", warning: true);
                    continue;
                }

                var result = await PluginUpdateService.ApplyAsync(pluginManager, eventDispatcher, routePolicy,
                    plugin.Name, update.AssetDownloadUrl, update.AssetName, update.LatestVersion, cancellationToken).ConfigureAwait(false);
                Report(result.Message, warning: result.PendingRestart);
                if (!result.PendingRestart) continue;
                runtimeState.RecommendRestart("plugin-update:" + plugin.Name, result.Message);
                pending.Add("插件 " + plugin.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Report($"插件 {plugin.Name} 自动更新失败: {ex.Message}", warning: true);
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
                Report($"适配器 {package.Id} 有新版本 {version}，但市场中没有可下载的包，已跳过。", warning: true);
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
                    Report($"适配器 {package.Id} 当前版本无法热替换，{probe.Version} 已暂存，将在重启宿主时替换。", warning: true);
                    pending.Add("适配器 " + package.Id);
                }
                else if (outcome.Result!.StartError is { } startError)
                    Report($"适配器 {package.Id} 已更新到 {probe.Version}，但启动失败: {startError}", warning: true);
                else
                    Report($"适配器 {package.Id} 已更新到 {probe.Version}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Report($"适配器 {package.Id} 自动更新失败: {ex.Message}", warning: true);
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
                Report($"主程序有新版本 {check.LatestVersion}，但无法自动更新: {check.Reason}", warning: true);
                return false;
            }

            // The update restarts the host, so the owners hear about this run first.
            Report($"主程序 {check.CurrentVersion} → {check.LatestVersion}，正在更新并重启…");
            await SendReportAsync(cancellationToken).ConfigureAwait(false);
            var result = await HostSelfUpdater.ApplyAsync(check.AssetDownloadUrl!, powerControl, cancellationToken).ConfigureAwait(false);
            if (result.Ok)
            {
                BotLog.Info($"主程序已更新到 {check.LatestVersion}: {result.Message}");
                return true;
            }

            Report("主程序自动更新失败: " + result.Message, warning: true);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report("主程序自动更新失败: " + ex.Message, warning: true);
            return false;
        }
    }
}
