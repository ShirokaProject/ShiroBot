using System.Text.Json.Nodes;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Components.Updates;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Plugins;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Update;

/// <summary>
///     One automatic update pass over the enabled kinds: plugins, then adapters, then the host. A host update restarts the
///     process
///     itself; otherwise updates that could only be staged restart the host when
///     <see cref="UpdateConfig.RestartOnPending" /> is on. What happened is sent to the owners as one
///     summary, before any restart; a run with nothing to update sends nothing. Excluded plugins and adapters are left
///     alone, and with <see cref="UpdateConfig.SkipMajor" /> a new major version is reported once instead of applied.
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
    OwnerNotify notifyOwners,
    SkippedMajorNotices skippedMajors)
{
    // Runs never overlap: the scheduler starts the next one only after this one returns.
    private readonly List<string> _report = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _report.Clear();
        try
        {
            var pending = new List<string>();
            if (config.Update.Plugins) await UpdatePluginsAsync(pending, cancellationToken).ConfigureAwait(false);
            if (config.Update.Adapters) await UpdateAdaptersAsync(pending, cancellationToken).ConfigureAwait(false);
            if (config.Update.Host && await UpdateHostAsync(cancellationToken).ConfigureAwait(false)) return;

            if (pending.Count == 0)
            {
                BotLog.Info("自动更新完成。");
                await SendReportAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var names = string.Join("、", pending);
            if (!config.Update.RestartOnPending)
            {
                Report($"{names} 无法热替换，新版本已暂存，需要重启宿主完成更新。", true);
                await SendReportAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            Report($"{names} 无法热替换，正在重启宿主完成更新…", true);
            await SendReportAsync(cancellationToken).ConfigureAwait(false);
            var restart = powerControl.Restart();
            if (restart.Ok) return;
            BotLog.Error("自动重启失败，请手动重启宿主: " + restart.Message);
            await notifyOwners("【ShiroBot 自动更新】自动重启失败，请手动重启宿主: " + restart.Message, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report("自动更新中断: " + ex.Message, true);
            await SendReportAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     True when this update is a major upgrade that must be applied by hand. The owners hear about each skipped
    ///     version once; later runs only log it.
    /// </summary>
    private bool SkipMajor(string key, string label, string current, string latest)
    {
        if (!config.Update.SkipMajor || !AutoUpdatePolicy.IsMajorUpgrade(current, latest)) return false;
        var message = $"{label} 有新的大版本 {current} → {latest}，可能包含不兼容的变化，未自动更新，请确认后手动更新。";
        if (skippedMajors.ShouldNotify(key, latest)) Report(message, true);
        else BotLog.Info(message);
        return true;
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
        foreach (var plugin in pluginManager.GetLoadedPluginSnapshot()
                     .Where(plugin => !string.IsNullOrWhiteSpace(plugin.GithubRepo)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AutoUpdatePolicy.IsExcluded(config.Update.ExcludePlugins, plugin.Name))
            {
                BotLog.Info($"插件 {plugin.Name} 已设为不自动更新，跳过。");
                continue;
            }

            try
            {
                var update = await Updater.CheckGitHubPluginPackageUpdateAsync(plugin.GithubRepo!, plugin.Version,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (update is null) continue;
                if (SkipMajor("plugin:" + plugin.Name, "插件 " + plugin.Name, plugin.Version,
                        update.LatestVersion)) continue;
                if (string.IsNullOrWhiteSpace(update.AssetDownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName))
                {
                    Report($"插件 {plugin.Name} 有新版本 {update.LatestVersion}，但 release 中没有可用的 zip 或 dll 插件包，已跳过。", true);
                    continue;
                }

                var result = await PluginUpdateService.ApplyAsync(pluginManager, eventDispatcher, routePolicy,
                        plugin.Name, update.AssetDownloadUrl, update.AssetName, update.LatestVersion, cancellationToken)
                    .ConfigureAwait(false);
                Report(result.Message, result.PendingRestart);
                if (!result.PendingRestart) continue;
                runtimeState.RecommendRestart("plugin-update:" + plugin.Name, result.Message);
                pending.Add("插件 " + plugin.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Report($"插件 {plugin.Name} 自动更新失败: {ex.Message}", true);
            }
        }
    }

    private async Task UpdateAdaptersAsync(List<string> pending, CancellationToken cancellationToken)
    {
        var installed = adapterPackages.List();
        if (installed.Count == 0) return;
        var entries = await AdapterMarketplaceCache.Shared.GetAsync(cancellationToken).ConfigureAwait(false);
        foreach (var package in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AutoUpdatePolicy.IsExcluded(config.Update.ExcludeAdapters, package.Id))
            {
                BotLog.Info($"适配器 {package.Id} 已设为不自动更新，跳过。");
                continue;
            }

            var entry = entries.OfType<JsonObject>().FirstOrDefault(entry =>
                string.Equals(entry["id"]?.GetValue<string>(), package.Id, StringComparison.OrdinalIgnoreCase));
            var hasRelease = entry?["release"]?["version"]?.GetValue<string>() is { Length: > 0 } version &&
                             entry["release"]?["prerelease"]?.GetValue<bool>() != true &&
                             Updater.IsNewerVersion(version, package.Version);
            if (!hasRelease) continue;
            var latest = entry!["release"]!["version"]!.GetValue<string>();
            if (SkipMajor("adapter:" + package.Id, "适配器 " + package.Id, package.Version, latest)) continue;
            if (MarketRelease.From(entry) is not { } release)
            {
                Report($"适配器 {package.Id} 有新版本 {entry["release"]!["version"]}，但市场中没有可下载的包，已跳过。", true);
                continue;
            }

            try
            {
                var (probe, outcome) = await AdapterPackageInstaller.InstallFromAssetAsync(adapterPackages,
                    adapterManager,
                    reloadCoordinator, runtimeState, release.AssetUrl, release.AssetName, release.Sha256, package.Id,
                    package.Enabled,
                    cancellationToken).ConfigureAwait(false);
                if (outcome.PendingRestart)
                {
                    Report($"适配器 {package.Id} 当前版本无法热替换，{probe.Version} 已暂存，将在重启宿主时替换。", true);
                    pending.Add("适配器 " + package.Id);
                }
                else if (outcome.Result!.StartError is { } startError)
                {
                    Report($"适配器 {package.Id} 已更新到 {probe.Version}，但启动失败: {startError}", true);
                }
                else
                {
                    Report($"适配器 {package.Id} 已更新到 {probe.Version}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Report($"适配器 {package.Id} 自动更新失败: {ex.Message}", true);
            }
        }
    }

    /// <summary>Returns true when the host is restarting into a new version.</summary>
    private async Task<bool> UpdateHostAsync(CancellationToken cancellationToken)
    {
        try
        {
            var check = await HostSelfUpdater.CheckAsync(config.HostUpdateRepository, cancellationToken)
                .ConfigureAwait(false);
            if (!check.UpdateAvailable) return false;
            if (SkipMajor("host", "主程序", check.CurrentVersion, check.LatestVersion ?? "")) return false;
            if (!check.CanApply)
            {
                Report($"主程序有新版本 {check.LatestVersion}，但无法自动更新: {check.Reason}", true);
                return false;
            }

            // The update restarts the host, so the owners hear about this run first.
            Report($"主程序 {check.CurrentVersion} → {check.LatestVersion}，正在更新并重启…");
            await SendReportAsync(cancellationToken).ConfigureAwait(false);
            var result = await HostSelfUpdater.ApplyAsync(check.AssetDownloadUrl!, powerControl, cancellationToken)
                .ConfigureAwait(false);
            if (result.Ok)
            {
                BotLog.Info($"主程序已更新到 {check.LatestVersion}: {result.Message}");
                return true;
            }

            Report("主程序自动更新失败: " + result.Message, true);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report("主程序自动更新失败: " + ex.Message, true);
            return false;
        }
    }
}