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
///     One automatic update pass over the enabled kinds. The host goes first: when it updates, the process restarts
///     straight away and <see cref="ResumeAsync" /> finishes the plugins and adapters in the new host, so a plugin built
///     for the new host is never loaded into the old one. Otherwise plugins, then adapters, are updated here; updates
///     that could only be staged restart the host when <see cref="UpdateConfig.RestartOnPending" /> is on. What
///     happened is sent to the owners as one summary, before any restart and again once a host update has come up,
///     where it says the host update succeeded and lists the plugins and adapters updated along with it; a run with
///     nothing to update sends nothing. Excluded plugins and adapters are left alone, and with
///     <see cref="UpdateConfig.SkipMajor" /> a new major version is reported once instead of applied.
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
    SkippedMajorNotices skippedMajors,
    HostUpdateResume hostUpdateResume)
{
    // Runs never overlap: the scheduler starts the next one only after this one returns.
    private readonly List<string> _report = [];
    private readonly List<string> _updated = [];

    // Set while resuming after a restart, so the summary leads with how the restart went.
    private string? _hostLine;
    private string? _hostNotes;
    private string _updatedHeading = UpdatedHeading;
    private string? _failedHeading;

    private const string UpdatedHeading = "以下插件和适配器已更新：";

    // The host's release notes go into the success report, cut at this length with a link to the release.
    private const int HostNotesLimit = 1500;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ResetReport();
        try
        {
            if (config.Update.Host && await UpdateHostAsync(cancellationToken).ConfigureAwait(false)) return;
            await UpdateComponentsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            Report("自动更新中断: " + ex.Message, true);
            await SendReportAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Finishes a run that a restart interrupted, if there is one. Once the startup plugin load is done, it reports
    ///     whether the host and the staged plugins and adapters came up on their new versions; after a host update it
    ///     then updates plugins and adapters as the run would have.
    /// </summary>
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (hostUpdateResume.Take() is not { } state) return;
        ResetReport();
        try
        {
            await eventDispatcher.InitialPluginsReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.To is { } target)
            {
                var current = HostSelfUpdater.CurrentVersion;
                var updated = !Updater.IsNewerVersion(target, current);
                _hostLine = updated
                    ? $"主程序已从 {state.From} 更新到 {current}，更新成功。"
                    : $"主程序重启后仍是 {current}，更新到 {target} 没有生效，请检查宿主日志。";
                if (updated) _updatedHeading = "以下插件和适配器也一并更新了：";
                if (updated) _hostNotes = ReleaseNotesText.ToPlain(state.ReleaseNotes, HostNotesLimit, state.ReleaseUrl);
                if (updated) BotLog.Info(_hostLine);
                else BotLog.Warning(_hostLine);
            }

            if (state.Staged is { Count: > 0 } staged)
            {
                if (state.To is null)
                {
                    _updatedHeading = "重启完成，" + UpdatedHeading;
                    _failedHeading = "重启完成，但以下更新没有成功：";
                }

                ReportStaged(staged);
            }

            if (state.To is not null) await UpdateComponentsAsync(cancellationToken).ConfigureAwait(false);
            else await SendReportAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            Report("自动更新中断: " + ex.Message, true);
            await SendReportAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Checks each staged plugin and adapter against what the restarted host is running: on its new version it
    ///     counts as updated, otherwise the replacement or the load failed and the owners are told which.
    /// </summary>
    private void ReportStaged(IReadOnlyList<StagedComponentUpdate> staged)
    {
        var plugins = pluginManager.GetLoadedPluginSnapshot();
        var adapters = adapterPackages.List();
        foreach (var item in staged)
        {
            string? running;
            string? loadError = null;
            if (item.Kind == StagedComponentUpdate.Adapter)
            {
                running = adapters.FirstOrDefault(package =>
                    string.Equals(package.Id, item.Id, StringComparison.OrdinalIgnoreCase))?.Version;
            }
            else
            {
                running = plugins.FirstOrDefault(plugin =>
                    string.Equals(plugin.Name, item.Id, StringComparison.OrdinalIgnoreCase))?.Version;
                if (running is null) loadError = pluginManager.GetPluginLoadError(item.Id);
            }

            if (running is not null && !Updater.IsNewerVersion(item.To, running))
                Updated(item.Label, item.From, running);
            else if (running is not null)
                Report($"{item.Label} 替换为 {item.To} 失败，仍是 {running}，下次启动会重试，详情见宿主日志。", true);
            else
                Report($"{item.Label} {item.To} 加载失败: {loadError ?? "请检查宿主日志。"}", true);
        }
    }

    /// <summary>Updates plugins and adapters, then sends the report, restarting for staged updates if allowed.</summary>
    private async Task UpdateComponentsAsync(CancellationToken cancellationToken)
    {
        var pending = new List<StagedComponentUpdate>();
        if (config.Update.Plugins) await UpdatePluginsAsync(pending, cancellationToken).ConfigureAwait(false);
        if (config.Update.Adapters) await UpdateAdaptersAsync(pending, cancellationToken).ConfigureAwait(false);

        if (pending.Count == 0)
        {
            BotLog.Info("自动更新完成。");
            await SendReportAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Recorded either way, so whichever restart applies them, automatic or by hand, reports how they came up.
        hostUpdateResume.AddStaged(pending);
        var names = string.Join("、", pending.Select(item => $"{item.Label} {item.To}"));
        if (!config.Update.RestartOnPending)
        {
            Report($"{names} 无法热替换，新版本已暂存，需要重启宿主完成更新，重启后会发送更新结果。", true);
            await SendReportAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        Report($"{names} 无法热替换，新版本已暂存，正在重启宿主完成更新…", true);
        await SendReportAsync(cancellationToken).ConfigureAwait(false);
        var restart = powerControl.Restart();
        if (restart.Ok) return;
        BotLog.Error("自动重启失败，请手动重启宿主: " + restart.Message);
        await notifyOwners("【ShiroBot 自动更新】自动重启失败，请手动重启宿主: " + restart.Message, null, cancellationToken)
            .ConfigureAwait(false);
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

    /// <summary>Records a plugin or adapter that is now running its new version.</summary>
    private void Updated(string label, string from, string to)
    {
        BotLog.Info($"{label} 已从 {from} 更新到 {to}");
        _updated.Add($"{label} {from} → {to}");
    }

    private void ResetReport()
    {
        _report.Clear();
        _updated.Clear();
        _hostLine = null;
        _hostNotes = null;
        _updatedHeading = UpdatedHeading;
        _failedHeading = null;
    }

    /// <summary>
    ///     Sends what has happened since the last report to the owners, if anything: the host update first when this
    ///     run resumed one, then the plugins and adapters that were updated, then everything else.
    /// </summary>
    private async Task SendReportAsync(CancellationToken cancellationToken)
    {
        if (_hostLine is null && _updated.Count == 0 && _report.Count == 0) return;
        var lines = new List<string> { "【ShiroBot 自动更新】" };
        if (_hostLine is not null) lines.Add(_hostLine);
        if (_hostNotes is not null) lines.Add("更新内容：\n" + _hostNotes);
        if (_updated.Count > 0)
        {
            lines.Add(_updatedHeading);
            lines.AddRange(_updated.Select(line => "· " + line));
        }

        if (_report.Count > 0)
        {
            if (lines.Count > 1) lines.Add("另外：");
            else if (_failedHeading is not null) lines.Add(_failedHeading);
            lines.AddRange(_report.Select(line => "· " + line));
        }

        ResetReport();
        await notifyOwners(string.Join("\n", lines), null, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdatePluginsAsync(List<StagedComponentUpdate> pending, CancellationToken cancellationToken)
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
                if (!result.PendingRestart)
                {
                    Updated("插件 " + plugin.Name, plugin.Version, update.LatestVersion);
                    continue;
                }

                BotLog.Warning(result.Message);
                runtimeState.RecommendRestart("plugin-update:" + plugin.Name, result.Message);
                pending.Add(new StagedComponentUpdate(StagedComponentUpdate.Plugin, plugin.Name, plugin.Version,
                    update.LatestVersion));
            }
            catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
            {
                Report($"插件 {plugin.Name} 自动更新失败: {ex.Message}", true);
            }
        }
    }

    private async Task UpdateAdaptersAsync(List<StagedComponentUpdate> pending, CancellationToken cancellationToken)
    {
        var installed = adapterPackages.List();
        if (installed.Count == 0) return;
        JsonArray entries;
        try
        {
            entries = await AdapterMarketplaceCache.Shared.GetAsync(cancellationToken, true).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            // The host update below does not depend on the adapter market being reachable.
            Report("适配器市场读取失败，本次未检查适配器更新: " + ex.Message, true);
            return;
        }

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
                    BotLog.Warning($"适配器 {package.Id} 当前版本无法热替换，{probe.Version} 已暂存，将在重启宿主时替换。");
                    pending.Add(new StagedComponentUpdate(StagedComponentUpdate.Adapter, package.Id, package.Version,
                        probe.Version));
                }
                else if (outcome.Result!.StartError is { } startError)
                {
                    Report($"适配器 {package.Id} 已更新到 {probe.Version}，但启动失败: {startError}", true);
                }
                else
                {
                    Updated("适配器 " + package.Id, package.Version, probe.Version);
                }
            }
            catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
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

            // The update restarts the host, so the owners hear about it first; the restarted host reports the
            // outcome and updates plugins and adapters from there.
            var resumes = config.Update.Plugins || config.Update.Adapters;
            Report($"主程序 {check.CurrentVersion} → {check.LatestVersion}，正在更新并重启" +
                   (resumes ? "，插件和适配器会在重启后一起更新…" : "…"));
            await SendReportAsync(cancellationToken).ConfigureAwait(false);
            hostUpdateResume.BeginHostUpdate(check.CurrentVersion, check.LatestVersion!, check.ReleaseNotes,
                check.ReleaseUrl);
            var result = await HostSelfUpdater.ApplyAsync(check.AssetDownloadUrl!, powerControl, cancellationToken)
                .ConfigureAwait(false);
            if (result.Ok)
            {
                BotLog.Info($"主程序已更新到 {check.LatestVersion}: {result.Message}");
                return true;
            }

            hostUpdateResume.CancelHostUpdate();
            Report("主程序自动更新失败: " + result.Message, true);
            return false;
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            hostUpdateResume.CancelHostUpdate();
            Report("主程序自动更新失败: " + ex.Message, true);
            return false;
        }
    }

    /// <summary>
    ///     Only a run that was itself cancelled counts. A timeout inside one update also surfaces as a cancellation
    ///     exception, and must be reported as that update's failure instead of silently ending the whole run.
    /// </summary>
    private static bool IsCancellation(Exception exception, CancellationToken cancellationToken)
    {
        return exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
    }
}