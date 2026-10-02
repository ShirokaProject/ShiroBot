using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ShiroBot.Configuration;
using ShiroBot.Console;
using ShiroBot.Update;
using ShiroBot.Hosting.Events;
using ShiroBot.Plugins;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using CH = ShiroBot.Console.ConsoleOutput;
using ShiroBot.Hosting.Runtime;

namespace ShiroBot.Hosting.Commands;

internal sealed class HostCommandHandler(
    PluginManager pluginManager,
    HostEventDispatcher eventDispatcher,
    PluginRouteConfig routePolicy,
    CoreConfig coreConfig,
    ConfigManager configManager,
    string configPath)
{
    private static readonly IReadOnlyList<CH.ConsoleCommandOption> ConsoleCommands =
    [
        new("help", "显示帮助信息"),
        new("plugins", "显示已加载插件"),
        new("adapters", "显示已安装适配器"),
        new("adapter", "管理适配器: start|stop|reload <id>"),
        new("actions", "显示插件注册的操作"),
        new("action", "执行插件操作: <插件> <操作>"),
        new("load", "热加载指定插件"),
        new("unload", "热卸载指定插件"),
        new("restart", "重启程序"),
        new("api", "显示或设置 API 鉴权信息"),
        new("update", "查看或处理待确认更新"),
        new("path", "打开当前程序目录"),
        new("log", "切换日志输出"),
        new("clear", "清除控制台"),
        new("exit", "退出程序"),
        new("quit", "退出程序")
    ];

    private AdapterManager? _adapterManager;
    private HostPowerControl? _powerControl;

    public void SetPowerControl(HostPowerControl powerControl) => _powerControl = powerControl;

    private ComponentReloadCoordinator? _reloadCoordinator;
    private AdapterPackageManager? _adapterPackages;

    public void SetAdapterCommands(AdapterManager adapterManager, ComponentReloadCoordinator? reloadCoordinator, AdapterPackageManager? adapterPackages = null)
    {
        _adapterManager = adapterManager;
        _reloadCoordinator = reloadCoordinator;
        _adapterPackages = adapterPackages;
    }

    public void RunConsoleLoop(
        TaskCompletionSource<bool> exitRequested,
        TaskCompletionSource<bool> consoleReady)
    {
        consoleReady.TrySetResult(true);

        while (true)
        {
            var input = CH.ReadPrompt(
                "> ",
                () => BuildConsoleCompletions(
                    pluginManager.GetLoadedPluginNames(),
                    pluginManager.GetLoadablePluginCandidates(includePluginNames: true),
                    CollectPluginActionsAsync().GetAwaiter().GetResult()));
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (CH.IsEnabled ||
                input.StartsWith("log", StringComparison.CurrentCultureIgnoreCase))
            {
                var splitInput = input.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
                switch (NormalizeCommand(splitInput.FirstOrDefault()))
                {
                    case "exit":
                    case "quit":
                        exitRequested.TrySetResult(true);
                        return;
                    case "plugins":
                        CH.Info(BuildLoadedPluginsText(pluginManager.GetLoadedPluginSnapshot()));
                        break;
                    case "adapters":
                        CH.Info(BuildAdaptersText());
                        break;
                    case "adapter":
                        CH.Info(HandleAdapterCommandAsync(splitInput).GetAwaiter().GetResult());
                        break;
                    case "load":
                        if (splitInput.Length < 2)
                        {
                            CH.Warning("用法: load <插件名|dll路径>");
                            break;
                        }

                        pluginManager.ScheduleLoadPluginByName(
                            eventDispatcher,
                            routePolicy,
                            splitInput[1]).GetAwaiter().GetResult();
                        break;
                    case "unload":
                        if (splitInput.Length < 2)
                        {
                            CH.Warning("用法: unload <插件名>");
                            break;
                        }

                        pluginManager.ScheduleUnloadPluginByName(
                            eventDispatcher,
                            splitInput[1]).GetAwaiter().GetResult();
                        break;
                    case "restart":
                    {
                        if (_powerControl is null)
                        {
                            CH.Error("宿主尚未启动完成，暂时无法重启。");
                            break;
                        }

                        var result = _powerControl.Restart();
                        if (result.Ok)
                        {
                            CH.Info(result.Message);
                            return;
                        }

                        CH.Error(result.Message);
                        break;
                    }
                    case "api":
                        CH.Info(HandleApiCommand(splitInput));
                        break;
                    case "actions":
                        CH.Info(BuildPluginActionListAsync().GetAwaiter().GetResult());
                        break;
                    case "action":
                        CH.Info(ExecutePluginActionAsync(splitInput).GetAwaiter().GetResult());
                        break;
                    case "update":
                        CH.Info(HandleUpdateCommandAsync(splitInput).GetAwaiter().GetResult());
                        break;
                    case "help":
                        var orderedCommands = ConsoleCommands
                            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var pluginActions = CollectPluginActionsAsync().GetAwaiter().GetResult();
                        // Plugin entries are the long ones, so size the column across both.
                        var nameWidth = Math.Max(
                            Math.Max(orderedCommands.Max(command => command.Name.Length), 8),
                            pluginActions.Count == 0
                                ? 0
                                : pluginActions.Max(entry => $"action {entry.PluginId} {entry.Action.Id}".Length)) + 2;
                        var helpText = new StringBuilder()
                            .AppendLine("可用命令")
                            .AppendLine(new string('-', 24));

                        foreach (var command in orderedCommands)
                            helpText.Append("  ")
                                .Append(command.Name.PadRight(nameWidth))
                                .AppendLine(command.Description);

                        AppendPluginActions(helpText, pluginActions, nameWidth);

                        CH.Info(helpText.ToString().TrimEnd());
                        break;
                    case "path":
                        var path = AppContext.BaseDirectory;
                        CH.Log("打开当前程序目录: " + path);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true
                        });
                        break;
                    case "log":
                        CH.IsEnabled = !CH.IsEnabled;
                        CH.Log(CH.IsEnabled ? "已开启日志输出" : "已关闭日志输出");
                        break;
                    case "clear":
                        CH.Clear();
                        break;
                    default:
                        CH.Warning($"未知命令: {input}");
                        break;
                }
            }
            else
            {
                CH.Warning("Log已被关闭，请输入 log 开启");
            }
        }
    }

    public Task HandleDirectMessageAsync(MessageEvent message) =>
        eventDispatcher.PublishAsync(message);

    private string HandleApiCommand(string[] splitInput)
    {
        if (splitInput.Length == 1)
        {
            return BuildApiInfoText();
        }

        if (!string.Equals(splitInput[1], "token", StringComparison.OrdinalIgnoreCase))
        {
            return "用法: api | api token | api token <密钥>";
        }

        coreConfig.Api.Auth.Key = splitInput.Length >= 3
            ? splitInput[2]
            : GenerateApiKey();
        coreConfig.Api.Auth.Enable = true;
        configManager.SaveConfig(configPath, coreConfig);

        return "API 鉴权密钥已更新。" + Environment.NewLine + BuildApiInfoText();
    }

    private string BuildApiInfoText()
    {
        var baseUrl = string.IsNullOrWhiteSpace(coreConfig.Api.PublicBaseUrl)
            ? coreConfig.Api.ListenUrls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)) ?? ApiHostConfig.DefaultListenUrl
            : coreConfig.Api.PublicBaseUrl;

        return new StringBuilder()
            .AppendLine("API 信息")
            .AppendLine(new string('-', 24))
            .AppendLine("启用: " + coreConfig.Api.Enable)
            .AppendLine("地址: " + baseUrl)
            .AppendLine("鉴权: " + coreConfig.Api.Auth.Enable)
            .AppendLine("密钥: " + (coreConfig.Api.Auth.Enable ? coreConfig.Api.Auth.Key : "未启用"))
            .AppendLine("调用: Authorization: Bearer <key>")
            .ToString()
            .TrimEnd();
    }

    private static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? NormalizeCommand(string? command) => command?.TrimStart('/').ToLowerInvariant();

    private string BuildAdaptersText()
    {
        var instances = _adapterPackages?.ListInstances();
        return instances is null || instances.Count == 0 ? "当前没有已安装适配器实例。" : string.Join("\n", instances.Select(instance =>
            $"{instance.Id} | 包 {instance.PackageId} | {(_adapterManager?.LoadedIds.Contains(instance.Id, StringComparer.OrdinalIgnoreCase) == true ? "运行中" : "已停止")} | 配置 {instance.ConfigPath}"));
    }

    private async Task<string> HandleAdapterCommandAsync(string[] input)
    {
        var manager = _adapterManager;
        var packages = _adapterPackages;
        if (manager is null) return "Adapter 管理器不可用。";
        if (input.Length < 2 || string.Equals(input[1], "list", StringComparison.OrdinalIgnoreCase)) return BuildAdaptersText();
        if (input.Length < 3) return "用法: adapter list | create <包 ID> <实例 ID> | config|start|stop|reload|remove <实例 ID>";
        try
        {
            switch (input[1].ToLowerInvariant())
            {
                case "create":
                    if (input.Length < 4) return "用法: adapter create <包 ID> <实例 ID>";
                    if (packages is null) throw new InvalidOperationException("Adapter 包管理器不可用。");
                    InstalledAdapterInstance? created = null;
                    Task CreateInstance() { created = packages.CreateInstance(input[2], input[3], null); return Task.CompletedTask; }
                    if (_reloadCoordinator is not null) await _reloadCoordinator.ExecuteAdapterMutationAsync(CreateInstance);
                    else await CreateInstance();
                    return $"已创建停用实例 {created!.Id}，请编辑 {created.ConfigPath}，然后 adapter start {created.Id}";
                case "config":
                    return packages?.GetInstance(input[2])?.ConfigPath ?? $"未安装 Adapter 实例: {input[2]}";
                case "remove":
                    if (packages is null) throw new InvalidOperationException("Adapter 包管理器不可用。");
                    async Task RemoveInstance()
                    {
                        await manager.StopByIdAsync(input[2]);
                        packages.DeleteInstance(input[2]);
                        manager.ForgetRemovedAdapter(input[2]);
                    }
                    if (_reloadCoordinator is not null) await _reloadCoordinator.ExecuteAdapterMutationAsync(RemoveInstance);
                    else await RemoveInstance();
                    return $"已删除 Adapter 实例: {input[2]}";
                case "start":
                    var package = packages?.GetInstance(input[2]) ?? throw new InvalidOperationException($"未安装 Adapter: {input[2]}");
                    if (_reloadCoordinator is not null)
                        await _reloadCoordinator.ExecuteAdapterMutationAsync(() => manager.LoadInstanceAsync(package));
                    else
                        await manager.LoadInstanceAsync(package);
                    packages!.SetInstanceEnabled(package.Id, true);
                    return $"已启动 Adapter: {package.Id}";
                case "stop":
                    if (_reloadCoordinator is not null)
                        await _reloadCoordinator.ExecuteAdapterMutationAsync(() => manager.StopByIdAsync(input[2]));
                    else
                        await manager.StopByIdAsync(input[2]);
                    packages?.SetInstanceEnabled(input[2], false);
                    return $"已停止 Adapter: {input[2]}";
                case "reload":
                    if (_reloadCoordinator is not null) await _reloadCoordinator.ReloadAdapterByIdAsync(input[2]); else await manager.ReloadByIdAsync(input[2]);
                    return $"已重载 Adapter: {input[2]}";
                default: return "用法: adapter list | create <包 ID> <实例 ID> | config|start|stop|reload|remove <实例 ID>";
            }
        }
        catch (Exception ex) { return "Adapter 操作失败: " + ex.Message; }
    }

    private async Task<string> HandleUpdateCommandAsync(string[] splitInput)
    {
        if (splitInput.Length < 2 || string.Equals(splitInput[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPendingUpdatesText();
        }

        switch (splitInput[1].ToLowerInvariant())
        {
            case "check":
                return splitInput.Length >= 3 && string.Equals(splitInput[2], "host", StringComparison.OrdinalIgnoreCase)
                    ? await CheckHostUpdateAsync()
                    : splitInput.Length >= 3 && string.Equals(splitInput[2], "plugins", StringComparison.OrdinalIgnoreCase)
                        ? await CheckPluginUpdatesAsync()
                        : await CheckAllUpdatesAsync();
            case "confirm":
            {
                if (splitInput.Length < 3) return "用法: update confirm <id>";

                try
                {
                    var ok = await Updater.ConfirmUpdateAsync(splitInput[2]);
                    if (!ok) return "未找到该更新请求。";

                    return "已执行更新任务。";
                }
                catch (Exception ex)
                {
                    return "更新执行失败: " + ex.Message;
                }
            }
            case "cancel":
            {
                if (splitInput.Length < 3) return "用法: update cancel <id>";

                var ok = Updater.CancelUpdate(splitInput[2]);
                return ok ? "已取消更新请求。" : "未找到该更新请求。";
            }
            default:
                return "用法: update [list|check [host|plugins]|confirm|cancel]";
        }
    }

    private async Task<string> CheckAllUpdatesAsync()
    {
        var host = await CheckHostUpdateAsync();
        var plugins = await CheckPluginUpdatesAsync();
        return host + "\n" + plugins;
    }

    private async Task<string> CheckHostUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(coreConfig.HostUpdateRepository))
        {
            return "宿主没有配置 HostUpdateRepository。";
        }

        var check = await HostSelfUpdater.CheckAsync(coreConfig.HostUpdateRepository).ConfigureAwait(false);
        if (!check.UpdateAvailable) return check.Reason ?? $"宿主: 已是最新版本 ({check.CurrentVersion})";
        if (!check.CanApply) return $"宿主: 发现 {check.LatestVersion}。{check.Reason}";
        var update = new GitHubReleaseUpdate(coreConfig.HostUpdateRepository, check.CurrentVersion, check.LatestVersion!,
            null, check.ReleaseUrl, check.ReleaseNotes, check.AssetDownloadUrl, check.AssetName);
        var id = await Updater.RequestHostUpdateAsync(update);
        return $"宿主: {check.CurrentVersion} -> {check.LatestVersion} ({check.AssetName})，更新任务 {id}";
    }

    private async Task<string> CheckPluginUpdatesAsync()
    {
        var plugins = pluginManager.GetLoadedPluginSnapshot()
            .Where(plugin => !string.IsNullOrWhiteSpace(plugin.GithubRepo))
            .ToList();

        if (plugins.Count == 0)
        {
            return "当前没有配置 GithubRepo 的已加载插件。";
        }

        var builder = new StringBuilder();
        foreach (var plugin in plugins)
        {
            try
            {
                var update = await Updater.CheckGitHubPluginPackageUpdateAsync(plugin.GithubRepo!, plugin.Version);
                if (update is null)
                {
                    builder.AppendLine($"{plugin.Name}: 已是最新版本 ({plugin.Version})");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(update.AssetDownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName))
                {
                    builder.AppendLine($"{plugin.Name}: 发现 {update.LatestVersion}，但 release 中没有 .zip 或 .dll 插件包。");
                    continue;
                }

                var request = new PluginUpdateRequest(
                    plugin.Name,
                    update.CurrentVersion,
                    update.LatestVersion,
                    update.ReleaseUrl,
                    update.ReleaseNotes,
                    update.AssetDownloadUrl,
                    plugin.AssemblyPath);
                var id = await Updater.RequestPluginUpdateAsync(request);

                builder.AppendLine($"{plugin.Name}: {update.CurrentVersion} -> {update.LatestVersion} ({update.AssetName})，更新任务 {id}");
            }
            catch (Exception ex)
            {
                builder.AppendLine($"{plugin.Name}: 检查失败 - {ex.Message}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildPendingUpdatesText()
    {
        var pending = Updater.GetPendingUpdates();
        return pending.Count == 0
            ? "当前没有待确认的更新请求。"
            : string.Join("\n", pending.Select(item =>
                $"{item.Id} | {item.Target} | {item.Name} {item.CurrentVersion} -> {item.LatestVersion}"));
    }

    private async Task<IReadOnlyList<(string PluginId, PluginActionDescriptor Action)>> CollectPluginActionsAsync()
    {
        var collected = new List<(string PluginId, PluginActionDescriptor Action)>();

        foreach (var plugin in pluginManager.GetLoadedPluginSnapshot()
                     .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!plugin.Supports<IPluginActionProvider>()) continue;

            var dispatch = await plugin
                .DispatchAsync<IPluginActionProvider, IReadOnlyList<PluginActionDescriptor>>(
                    provider => Task.FromResult(provider.Actions))
                .ConfigureAwait(false);
            if (!dispatch.Dispatched || dispatch.Result is null) continue;

            var actions = dispatch.Result
                .Where(action => !string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(action.Label))
                .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());

            foreach (var action in actions) collected.Add((plugin.Name, action));
        }

        return collected;
    }

    private static void AppendPluginActions(
        StringBuilder text,
        IReadOnlyList<(string PluginId, PluginActionDescriptor Action)> actions,
        int nameWidth)
    {
        if (actions.Count == 0) return;

        text.AppendLine().AppendLine("插件操作");
        foreach (var group in actions.GroupBy(entry => entry.PluginId, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (pluginId, action) in group)
            {
                text.Append("  ")
                    .Append($"action {pluginId} {action.Id}".PadRight(nameWidth))
                    .AppendLine(string.IsNullOrWhiteSpace(action.Description) ? action.Label : action.Description);
            }
        }
    }

    private async Task<string> BuildPluginActionListAsync()
    {
        var actions = await CollectPluginActionsAsync().ConfigureAwait(false);
        if (actions.Count == 0) return "当前没有插件注册操作。";

        var nameWidth = actions.Max(entry => $"action {entry.PluginId} {entry.Action.Id}".Length) + 2;
        var text = new StringBuilder().AppendLine("插件操作").AppendLine(new string('-', 24));
        foreach (var group in actions.GroupBy(entry => entry.PluginId, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (pluginId, action) in group)
            {
                text.Append("  ")
                    .Append($"action {pluginId} {action.Id}".PadRight(nameWidth))
                    .AppendLine(string.IsNullOrWhiteSpace(action.Description) ? action.Label : action.Description);
            }
        }

        return text.ToString().TrimEnd();
    }

    private async Task<string> ExecutePluginActionAsync(string[] splitInput)
    {
        if (splitInput.Length < 3) return "用法: action <插件> <操作>，可用 actions 查看列表";

        var pluginId = splitInput[1];
        var actionId = splitInput[2];

        var plugin = pluginManager.GetLoadedPluginSnapshot().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, pluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin is null) return $"未找到已加载插件: {pluginId}";
        if (!plugin.Supports<IPluginActionProvider>()) return $"插件 {pluginId} 未提供操作。";

        var descriptors = await plugin
            .DispatchAsync<IPluginActionProvider, IReadOnlyList<PluginActionDescriptor>>(
                provider => Task.FromResult(provider.Actions))
            .ConfigureAwait(false);
        if (!descriptors.Dispatched) return $"插件 {pluginId} 正在卸载。";

        var descriptor = descriptors.Result?.FirstOrDefault(action =>
            string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) return $"未找到插件操作: {actionId}";

        if (descriptor.RequiresConfirmation)
        {
            var prompt = string.IsNullOrWhiteSpace(descriptor.ConfirmationText)
                ? $"确认执行 {descriptor.Label}?"
                : descriptor.ConfirmationText;
            var answer = CH.ReadPrompt($"{prompt} [y/N] ")?.Trim();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)) return "已取消。";
        }

        var execution = await plugin
            .DispatchAsync<IPluginActionProvider, PluginActionResult?>(
                async provider => await provider.ExecuteActionAsync(actionId).ConfigureAwait(false))
            .ConfigureAwait(false);
        if (!execution.Dispatched) return $"插件 {pluginId} 正在卸载。";

        var result = execution.Result;
        if (result is null) return $"插件操作没有返回结果: {actionId}";

        return result.Ok ? result.Message : $"操作失败: {result.Message}";
    }

    private static IReadOnlyList<CH.ConsoleCommandOption> BuildConsoleCompletions(
        IReadOnlyList<string> loadedPluginNames,
        IReadOnlyList<string> loadablePluginCandidates,
        IReadOnlyList<(string PluginId, PluginActionDescriptor Action)> pluginActions)
    {
        var completions = new List<ConsoleOutput.ConsoleCommandOption>(ConsoleCommands);
        var loadedNameSet = new HashSet<string>(loadedPluginNames, StringComparer.OrdinalIgnoreCase);

        foreach (var pluginName in loadedPluginNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            completions.Add(
                new ConsoleOutput.ConsoleCommandOption($"unload {pluginName}", $"热卸载插件 {pluginName}"));
        }

        foreach (var candidate in loadablePluginCandidates.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (IsAlreadyLoadedCandidate(candidate, loadedNameSet)) continue;

            completions.Add(new ConsoleOutput.ConsoleCommandOption($"load {candidate}", $"热加载插件 {candidate}"));
        }

        foreach (var (pluginId, action) in pluginActions)
        {
            completions.Add(new ConsoleOutput.ConsoleCommandOption(
                $"action {pluginId} {action.Id}",
                string.IsNullOrWhiteSpace(action.Description) ? action.Label : action.Description));
        }

        return completions;
    }

    private static bool IsAlreadyLoadedCandidate(string candidate, HashSet<string> loadedPluginNames)
    {
        if (loadedPluginNames.Contains(candidate)) return true;

        var withoutExtension = Path.GetFileNameWithoutExtension(candidate);
        if (!string.IsNullOrWhiteSpace(withoutExtension) && loadedPluginNames.Contains(withoutExtension)) return true;

        var directoryName = Path.GetDirectoryName(candidate.Replace('/', Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            var lastDirectory = Path.GetFileName(directoryName);
            if (!string.IsNullOrWhiteSpace(lastDirectory) && loadedPluginNames.Contains(lastDirectory)) return true;
        }

        return false;
    }

    private static string BuildLoadedPluginsText(IReadOnlyList<LoadedPluginHandle> plugins)
    {
        if (plugins.Count == 0)
        {
            return "当前没有已加载插件。";
        }

        var orderedPlugins = plugins
            .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var builder = new StringBuilder()
            .AppendLine("已加载插件")
            .AppendLine("名称 | 版本 | 程序集文件大小");
        long totalBytes = 0;

        foreach (var plugin in orderedPlugins)
        {
            var bytes = plugin.GetLoadedAssemblyFileBytes();
            totalBytes += bytes;
            builder.Append(plugin.Name)
                .Append(" | v")
                .Append(plugin.Version)
                .Append(" | ")
                .AppendLine(FormatBytes(bytes));
        }

        builder.Append("合计 | ")
            .Append(orderedPlugins.Count)
            .Append(" 个 | ")
            .AppendLine(FormatBytes(totalBytes));

        using var process = Process.GetCurrentProcess();
        builder.AppendLine()
            .Append("进程工作集（宿主 + 适配器 + 全部插件）: ")
            .Append(FormatBytes(process.WorkingSet64));

        return builder.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }
}
