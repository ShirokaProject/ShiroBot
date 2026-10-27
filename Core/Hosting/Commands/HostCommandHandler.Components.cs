using System.Text;
using System.Text.Json.Nodes;
using ShiroBot.Adapters;
using ShiroBot.Components.Updates;
using ShiroBot.Hosting.Http;
using ShiroBot.Plugins.Marketplace;
using ShiroBot.SDK.Plugin;
using ShiroBot.Update;
using CH = ShiroBot.TerminalUI.ConsoleOutput;

namespace ShiroBot.Hosting.Commands;

/// <summary>
/// The plugin, adapter and update commands, grouped by what they act on (plugin install, adapter start, ...),
/// with their markets and completions. Installs go through the same installers as the Dashboard and auto-updates.
/// </summary>
internal sealed partial class HostCommandHandler
{
    private const string PluginUsage =
        "用法: plugin list | market | install <市场 ID> | load <插件> | unload <插件> | update [confirm|cancel <ID>] | actions | action <插件> <操作>";
    private const string AdapterUsage =
        "用法: adapter list | market | install <市场 ID> | create <包 ID> <实例 ID> | config|start|stop|reload|remove <实例 ID>";

    /// <summary>Commands that moved under plugin and adapter, with what to type now.</summary>
    private static readonly Dictionary<string, string> MovedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plugins"] = "plugin list",
        ["load"] = "plugin load",
        ["unload"] = "plugin unload",
        ["actions"] = "plugin actions",
        ["action"] = "plugin action",
        ["adapters"] = "adapter list",
        ["help"] = "/（输入 / 列出全部命令）"
    };

    // Market lists for completion, fetched in the background so typing never waits on the network.
    private volatile JsonArray? _pluginMarket;
    private volatile JsonArray? _adapterMarket;

    private async Task<string> HandlePluginCommandAsync(string[] input)
    {
        var arguments = input.Skip(2).ToArray();
        switch (input.Length > 1 ? input[1].ToLowerInvariant() : "list")
        {
            case "list":
                return BuildLoadedPluginsText(pluginManager.GetLoadedPluginSnapshot());
            case "market":
                return await BuildPluginMarketTextAsync().ConfigureAwait(false);
            case "install":
                return arguments.Length == 0 ? "用法: plugin install <市场 ID>，可用 plugin market 查看" : await InstallPluginFromMarketAsync(arguments[0]).ConfigureAwait(false);
            case "load":
                if (arguments.Length == 0) return "用法: plugin load <插件名|dll 路径>";
                await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, arguments[0]).ConfigureAwait(false);
                return string.Empty; // the plugin manager reports the load itself
            case "unload":
                if (arguments.Length == 0) return "用法: plugin unload <插件>";
                await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, arguments[0]).ConfigureAwait(false);
                return string.Empty;
            case "update":
                return await HandlePendingUpdateAsync(UpdateTarget.Plugin, arguments, CheckPluginUpdatesAsync).ConfigureAwait(false);
            case "actions":
                return await BuildPluginActionListAsync().ConfigureAwait(false);
            case "action":
                return await ExecutePluginActionAsync(arguments).ConfigureAwait(false);
            default:
                return PluginUsage;
        }
    }

    /// <summary>update is the host's alone; plugin updates are plugin update.</summary>
    private Task<string> HandleUpdateCommandAsync(string[] input) =>
        HandlePendingUpdateAsync(UpdateTarget.Host, input.Skip(1).ToArray(), CheckHostUpdateAsync);

    /// <summary>Nothing or "check" looks for updates; "confirm|cancel &lt;ID&gt;" handles one waiting for this target.</summary>
    private static async Task<string> HandlePendingUpdateAsync(UpdateTarget target, string[] arguments, Func<Task<string>> check)
    {
        var command = target == UpdateTarget.Host ? "update" : "plugin update";
        var action = arguments.Length > 0 ? arguments[0].ToLowerInvariant() : "check";
        if (action == "check") return await check().ConfigureAwait(false);
        if (action is not ("confirm" or "cancel") || arguments.Length < 2) return $"用法: {command} [confirm|cancel <ID>]";

        var pending = Updater.GetPendingUpdates().FirstOrDefault(item =>
            item.Target == target && string.Equals(item.Id, arguments[1], StringComparison.OrdinalIgnoreCase));
        if (pending is null) return $"没有待确认的{(target == UpdateTarget.Host ? "主程序" : "插件")}更新 {arguments[1]}。";
        if (action == "cancel") return Updater.CancelUpdate(pending.Id) ? "已取消更新请求。" : "未找到该更新请求。";

        try
        {
            return await Updater.ConfirmUpdateAsync(pending.Id).ConfigureAwait(false) ? "已执行更新任务。" : "未找到该更新请求。";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "更新执行失败: " + ex.Message;
        }
    }

    private async Task<string> BuildPluginMarketTextAsync()
    {
        var plugins = await LoadPluginMarketAsync().ConfigureAwait(false);
        return BuildMarketText("插件市场", plugins, entry => entry["installed"]?["version"]?.GetValue<string>(), "plugin install <ID>");
    }

    private async Task<string> BuildAdapterMarketTextAsync()
    {
        var adapters = await LoadAdapterMarketAsync().ConfigureAwait(false);
        return BuildMarketText("适配器市场", adapters, entry => InstalledAdapterVersion(entry["id"]?.GetValue<string>()), "adapter install <ID>");
    }

    private static string BuildMarketText(string title, JsonArray entries, Func<JsonObject, string?> installedVersion, string installHint)
    {
        var listed = entries.OfType<JsonObject>().Where(entry => entry["id"] is not null).ToArray();
        if (listed.Length == 0) return title + "暂时不可用或为空。";

        var width = listed.Max(entry => entry["id"]!.GetValue<string>().Length) + 2;
        var text = new StringBuilder().AppendLine(title).AppendLine(new string('-', 24));
        foreach (var entry in listed)
            text.Append("  ").Append(entry["id"]!.GetValue<string>().PadRight(width))
                .Append(MarketState(entry, installedVersion(entry))).Append("  ")
                .AppendLine(entry["description"]?.GetValue<string>() ?? entry["name"]?.GetValue<string>());
        return text.Append("安装: ").Append(installHint).ToString();
    }

    /// <summary>The latest version, and whether the installed one is current or can be updated.</summary>
    private static string MarketState(JsonObject entry, string? installedVersion)
    {
        var latest = entry["release"]?["version"]?.GetValue<string>();
        if (installedVersion is null) return latest is null ? "无发布" : "v" + latest;
        return latest is not null && Updater.IsNewerVersion(latest, installedVersion)
            ? $"v{installedVersion} → v{latest} 可更新"
            : $"v{installedVersion} 已安装";
    }

    private async Task<string> InstallPluginFromMarketAsync(string id)
    {
        var entry = FindMarketEntry(await LoadPluginMarketAsync().ConfigureAwait(false), id);
        if (entry is null) return $"插件市场中没有 {id}，可用 plugin market 查看。";
        if (MarketRelease.From(entry) is not { } release) return $"{id} 在插件市场中没有可下载的发布包。";

        CH.Info($"正在安装插件 {id} {release.Version}…");
        try
        {
            var result = await PluginUpdateService.InstallFromAssetAsync(pluginManager, eventDispatcher, routePolicy,
                release.AssetUrl, release.AssetName, release.Sha256, expectedId: null).ConfigureAwait(false);
            if (result.PendingRestart) _runtimeState?.RecommendRestart("plugin-update:" + id, result.Message);
            RefreshMarketsInBackground();
            return result.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"插件 {id} 安装失败: {ex.Message}";
        }
    }

    private async Task<string> InstallAdapterFromMarketAsync(string id)
    {
        if (_adapterManager is not { } manager || _reloadCoordinator is not { } coordinator ||
            _adapterPackages is not { } packages || _runtimeState is not { } runtime)
            return "适配器管理器尚未就绪。";
        var entry = FindMarketEntry(await LoadAdapterMarketAsync().ConfigureAwait(false), id);
        if (entry is null) return $"适配器市场中没有 {id}，可用 adapter market 查看。";
        if (MarketRelease.From(entry) is not { } release) return $"{id} 在适配器市场中没有可下载的发布包。";

        CH.Info($"正在安装适配器 {id} {release.Version}…");
        try
        {
            var existed = packages.Get(id) is not null;
            var (probe, outcome) = await AdapterPackageInstaller.InstallFromAssetAsync(packages, manager, coordinator, runtime,
                release.AssetUrl, release.AssetName, release.Sha256, expectedId: null, enable: true).ConfigureAwait(false);
            RefreshMarketsInBackground();
            if (outcome.PendingRestart) return $"适配器 {probe.Id} 当前版本无法热替换，{probe.Version} 已暂存，将在重启宿主时替换。";
            if (outcome.Result!.StartError is { } startError) return $"适配器 {probe.Id} 已更新到 {probe.Version}，但启动失败: {startError}";
            return existed
                ? $"适配器 {probe.Id} 已更新到 {probe.Version}"
                : $"已安装适配器 {probe.Id} {probe.Version}，用 adapter create {probe.Id} <实例 ID> 创建实例。";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"适配器 {id} 安装失败: {ex.Message}";
        }
    }

    private static JsonObject? FindMarketEntry(JsonArray entries, string id) =>
        entries.OfType<JsonObject>().FirstOrDefault(entry =>
            string.Equals(entry["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));

    private string? InstalledAdapterVersion(string? id) => id is null ? null : _adapterPackages?.Get(id)?.Version;

    private async Task<JsonArray> LoadPluginMarketAsync()
    {
        var marketplace = await PluginMarketplaceCache.Shared.GetAsync(
            HostHttpServer.GetMarketplaceInstalledPlugins(pluginManager), CancellationToken.None).ConfigureAwait(false);
        return _pluginMarket = marketplace["plugins"] as JsonArray ?? [];
    }

    private async Task<JsonArray> LoadAdapterMarketAsync() =>
        _adapterMarket = await AdapterMarketplaceCache.Shared.GetAsync(CancellationToken.None).ConfigureAwait(false);

    /// <summary>Fills the market lists completion offers; a market that cannot be reached just offers nothing.</summary>
    private void RefreshMarketsInBackground() => _ = Task.Run(async () =>
    {
        try { await LoadPluginMarketAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        try { await LoadAdapterMarketAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    });

    /// <summary>
    /// Every command line with its arguments; the console offers them a word at a time. Sub-commands get their own
    /// line so they are described, rather than shown only as a count of what follows.
    /// </summary>
    private IReadOnlyList<CH.ConsoleCommandOption> BuildCompletions()
    {
        var options = new List<CH.ConsoleCommandOption>(ConsoleCommands);
        options.AddRange(BuildPluginCompletions());
        options.AddRange(BuildAdapterCompletions());
        options.AddRange(BuildPendingUpdateCompletions(UpdateTarget.Host, "update"));
        options.Add(new("api token", "生成新的 API 密钥"));
        return options;
    }

    private IEnumerable<CH.ConsoleCommandOption> BuildPluginCompletions()
    {
        yield return new("plugin list", "显示已加载插件");
        yield return new("plugin market", "浏览插件市场");
        yield return new("plugin install", "从插件市场安装: <ID>");
        yield return new("plugin load", "热加载插件");
        yield return new("plugin unload", "热卸载插件");
        yield return new("plugin update", "检查插件更新");
        yield return new("plugin actions", "显示插件注册的操作");
        yield return new("plugin action", "执行插件操作: <插件> <操作>");

        foreach (var entry in _pluginMarket?.OfType<JsonObject>() ?? [])
        {
            if (entry["id"]?.GetValue<string>() is not { } id || MarketRelease.From(entry) is null) continue;
            var installed = entry["installed"]?["version"]?.GetValue<string>();
            var state = MarketState(entry, installed);
            if (installed is not null && !state.EndsWith("可更新", StringComparison.Ordinal)) continue;
            yield return new($"plugin install {id}", $"{entry["name"]?.GetValue<string>() ?? id} · {state}");
        }

        var loaded = pluginManager.GetLoadedPluginSnapshot();
        var loadedNames = loaded.Select(plugin => plugin.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in loaded.OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase))
            yield return new($"plugin unload {plugin.Name}", $"热卸载 {plugin.DisplayName}");
        foreach (var candidate in pluginManager.GetLoadablePluginCandidates(includePluginNames: true)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            if (!IsAlreadyLoadedCandidate(candidate, loadedNames))
                yield return new($"plugin load {candidate}", $"热加载 {candidate}");

        foreach (var option in BuildPendingUpdateCompletions(UpdateTarget.Plugin, "plugin update")) yield return option;

        var actions = CollectPluginActionsAsync().GetAwaiter().GetResult();
        var displayNames = loaded.ToDictionary(plugin => plugin.Name, plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in actions.GroupBy(entry => entry.PluginId, StringComparer.OrdinalIgnoreCase))
        {
            // The plugin step of "plugin action <plugin> <action>": its name and how many actions it has.
            var name = displayNames.TryGetValue(plugin.Key, out var displayName) && !string.Equals(displayName, plugin.Key, StringComparison.Ordinal)
                ? displayName + " · "
                : string.Empty;
            yield return new($"plugin action {plugin.Key}", $"{name}{plugin.Count()} 个操作");
        }

        foreach (var (pluginId, action) in actions)
            yield return new($"plugin action {pluginId} {action.Id}",
                string.IsNullOrWhiteSpace(action.Description) ? action.Label : action.Description);
    }

    private IEnumerable<CH.ConsoleCommandOption> BuildAdapterCompletions()
    {
        yield return new("adapter list", "列出适配器实例");
        yield return new("adapter market", "浏览适配器市场");
        yield return new("adapter install", "从适配器市场安装: <ID>");
        yield return new("adapter create", "创建实例: <包 ID> <新实例 ID>");
        yield return new("adapter start", "启动已停止的实例");
        yield return new("adapter stop", "停止运行中的实例");
        yield return new("adapter reload", "重载运行中的实例");
        yield return new("adapter config", "显示实例的配置文件路径");
        yield return new("adapter remove", "删除实例及其配置");

        foreach (var entry in _adapterMarket?.OfType<JsonObject>() ?? [])
        {
            if (entry["id"]?.GetValue<string>() is not { } id || MarketRelease.From(entry) is null) continue;
            var state = MarketState(entry, InstalledAdapterVersion(id));
            if (InstalledAdapterVersion(id) is not null && !state.EndsWith("可更新", StringComparison.Ordinal)) continue;
            yield return new($"adapter install {id}", $"{entry["name"]?.GetValue<string>() ?? id} · {state}");
        }

        foreach (var package in _adapterPackages?.List() ?? [])
            yield return new($"adapter create {package.Id}", $"用适配器包 {package.Id} 创建实例，后接新实例 ID");
        var loaded = _adapterManager?.LoadedIds ?? [];
        foreach (var instance in _adapterPackages?.ListInstances() ?? [])
        {
            if (loaded.Contains(instance.Id, StringComparer.OrdinalIgnoreCase))
            {
                yield return new($"adapter stop {instance.Id}", $"停止 {instance.Name}");
                yield return new($"adapter reload {instance.Id}", $"重载 {instance.Name}");
            }
            else
            {
                yield return new($"adapter start {instance.Id}", $"启动 {instance.Name}");
            }

            yield return new($"adapter config {instance.Id}", $"显示 {instance.Name} 的配置文件路径");
            yield return new($"adapter remove {instance.Id}", $"删除实例 {instance.Name} 及其配置");
        }
    }

    private static IEnumerable<CH.ConsoleCommandOption> BuildPendingUpdateCompletions(UpdateTarget target, string command)
    {
        var pending = Updater.GetPendingUpdates().Where(item => item.Target == target).ToArray();
        if (pending.Length == 0) yield break;

        yield return new($"{command} confirm", "执行待确认的更新: <ID>");
        yield return new($"{command} cancel", "取消待确认的更新: <ID>");
        foreach (var item in pending)
        {
            var text = $"{item.Name} {item.CurrentVersion} → {item.LatestVersion}";
            yield return new($"{command} confirm {item.Id}", "执行更新: " + text);
            yield return new($"{command} cancel {item.Id}", "取消更新: " + text);
        }
    }
}
