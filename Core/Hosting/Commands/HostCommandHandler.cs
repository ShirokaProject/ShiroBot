using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ShiroBot.Configuration;
using ShiroBot.TerminalUI;
using ShiroBot.Update;
using ShiroBot.Hosting.Events;
using ShiroBot.Plugins;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using CH = ShiroBot.TerminalUI.ConsoleOutput;
using ShiroBot.Hosting.Runtime;

namespace ShiroBot.Hosting.Commands;

internal sealed partial class HostCommandHandler(
    PluginManager pluginManager,
    HostEventDispatcher eventDispatcher,
    PluginRouteConfig routePolicy,
    CoreConfig coreConfig,
    ConfigManager configManager,
    string configPath)
{
    private static readonly IReadOnlyList<CH.ConsoleCommandOption> ConsoleCommands =
    [
        new("plugin", "插件: list|market|install|load|unload|update|actions|action"),
        new("adapter", "适配器: list|market|install|create|start|stop|reload|config|remove"),
        new("update", "检查或更新主程序"),
        new("restart", "重启程序"),
        new("api", "显示或设置 API 鉴权信息"),
        new("dashboard", "在浏览器中打开 Dashboard（自动登录）"),
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

    private HostRuntimeState? _runtimeState;

    public void SetAdapterCommands(AdapterManager adapterManager, ComponentReloadCoordinator? reloadCoordinator, AdapterPackageManager? adapterPackages = null,
        HostRuntimeState? runtimeState = null)
    {
        _runtimeState = runtimeState ?? _runtimeState;
        _adapterManager = adapterManager;
        _reloadCoordinator = reloadCoordinator;
        _adapterPackages = adapterPackages;
    }

    public void RunConsoleLoop(
        TaskCompletionSource<bool> exitRequested,
        TaskCompletionSource<bool> consoleReady)
    {
        consoleReady.TrySetResult(true);
        RefreshMarketsInBackground();

        while (true)
        {
            var input = CH.ReadPrompt(
                "> ",
                BuildCompletions);
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (CH.IsEnabled ||
                input.StartsWith("log", StringComparison.CurrentCultureIgnoreCase))
            {
                var splitInput = input.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
                // One failing command (often a plugin's) must not end the loop: it runs in a fire-and-forget task,
                // so an escaped exception would silently stop the console from reading input.
                try
                {
                    switch (NormalizeCommand(splitInput.FirstOrDefault()))
                    {
                        case "exit":
                        case "quit":
                            exitRequested.TrySetResult(true);
                            return;
                        case "plugin":
                            Print(HandlePluginCommandAsync(splitInput).GetAwaiter().GetResult());
                            break;
                        case "adapter":
                            Print(HandleAdapterCommandAsync(splitInput).GetAwaiter().GetResult());
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
                        case "dashboard":
                            CH.Info(OpenDashboard());
                            break;
                        case "update":
                            Print(HandleUpdateCommandAsync(splitInput).GetAwaiter().GetResult());
                            break;
                        case "path":
                            var path = HostPaths.Root;
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
                        case var moved when MovedCommands.TryGetValue(moved ?? "", out var replacement):
                            CH.Warning($"{moved} 已改为 {replacement}");
                            break;
                        default:
                            CH.Warning($"未知命令: {input}，输入 / 查看全部命令");
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    CH.Error($"命令执行失败: {input} - {ex.Message}");
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

        coreConfig.Api.Token = splitInput.Length >= 3
            ? splitInput[2]
            : GenerateApiKey();
        configManager.SaveConfig(configPath, coreConfig);

        return "API 鉴权密钥已更新。" + Environment.NewLine + BuildApiInfoText();
    }

    private string BuildApiInfoText()
    {
        var baseUrl = coreConfig.Api.GetPrimaryBaseUrl();

        var text = new StringBuilder()
            .AppendLine("API 信息")
            .AppendLine(new string('-', 24))
            .AppendLine("启用: " + coreConfig.Api.Enable)
            .AppendLine("地址: " + baseUrl);
        if (coreConfig.Api.EnableDashboard) text.AppendLine("Dashboard（直接登录）: " + coreConfig.Api.GetDashboardLoginUrl());
        return text
            .AppendLine("鉴权: 始终开启")
            .AppendLine("密钥: " + coreConfig.Api.Token)
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

    private static void Print(string text)
    {
        if (!string.IsNullOrEmpty(text)) CH.Info(text);
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
        if (string.Equals(input[1], "market", StringComparison.OrdinalIgnoreCase)) return await BuildAdapterMarketTextAsync();
        if (input.Length < 3) return AdapterUsage;
        if (string.Equals(input[1], "install", StringComparison.OrdinalIgnoreCase)) return await InstallAdapterFromMarketAsync(input[2]);
        try
        {
            switch (input[1].ToLowerInvariant())
            {
                case "create":
                    if (input.Length < 4) return "用法: adapter create <包 ID> <实例 ID>";
                    if (packages is null) throw new InvalidOperationException("Adapter 包管理器不可用。");
                    InstalledAdapterInstance? created = null;
                    async Task CreateInstance() => created = await manager.CreateInstanceAsync(packages, input[2], input[3], null);
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
                    if (!package.PackageEnabled) throw new InvalidOperationException($"适配器包 {package.PackageId} 已关闭，请先打开适配器。");
                    if (_reloadCoordinator is not null)
                        await _reloadCoordinator.ExecuteAdapterStartAsync(() => manager.LoadInstanceAsync(package));
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
                default: return AdapterUsage;
            }
        }
        catch (Exception ex) { return "Adapter 操作失败: " + ex.Message; }
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

    private Task<IReadOnlyList<(string PluginId, PluginActionDescriptor Action)>> CollectPluginActionsAsync() =>
        PluginActions.ListAllAsync(pluginManager);

    private async Task<string> BuildPluginActionListAsync()
    {
        var actions = await CollectPluginActionsAsync().ConfigureAwait(false);
        if (actions.Count == 0) return "当前没有插件注册操作。";

        var nameWidth = actions.Max(entry => $"plugin action {entry.PluginId} {entry.Action.Id}".Length) + 2;
        var text = new StringBuilder().AppendLine("插件操作").AppendLine(new string('-', 24));
        foreach (var group in actions.GroupBy(entry => entry.PluginId, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (pluginId, action) in group)
            {
                text.Append("  ")
                    .Append($"plugin action {pluginId} {action.Id}".PadRight(nameWidth))
                    .AppendLine(string.IsNullOrWhiteSpace(action.Description) ? action.Label : action.Description);
            }
        }

        return text.ToString().TrimEnd();
    }

    private async Task<string> ExecutePluginActionAsync(string[] arguments)
    {
        if (arguments.Length < 2) return "用法: plugin action <插件> <操作>，可用 plugin actions 查看列表";

        var pluginId = arguments[0];
        var actionId = arguments[1];
        var plugin = PluginActions.FindPlugin(pluginManager, pluginId);
        if (plugin is null) return $"未找到已加载插件: {pluginId}";

        var actions = await PluginActions.ListAsync(plugin).ConfigureAwait(false);
        if (actions is null) return $"插件 {pluginId} 正在卸载。";
        var descriptor = actions.FirstOrDefault(action => string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) return $"未找到插件操作: {actionId}";

        if (descriptor.RequiresConfirmation)
        {
            var prompt = string.IsNullOrWhiteSpace(descriptor.ConfirmationText)
                ? $"确认执行 {descriptor.Label}?"
                : descriptor.ConfirmationText;
            var answer = CH.ReadPrompt($"{prompt} [y/N] ")?.Trim();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)) return "已取消。";
        }

        var outcome = await PluginActions.ExecuteAsync(plugin, descriptor.Id).ConfigureAwait(false);
        if (outcome.Result is not { } result) return outcome.Message;

        var summary = result.Ok ? result.Message : $"操作失败: {result.Message}";
        if (string.IsNullOrWhiteSpace(result.Details)) return summary;
        // Details are meant to be read: they float above the prompt, headed like the Dashboard dialog.
        CH.Notice(result.Title ?? descriptor.Label, summary, result.Details, !result.Ok);
        return summary;
    }

    /// <summary>Opens the Dashboard in the default browser, signed in by its token link.</summary>
    private string OpenDashboard()
    {
        if (!coreConfig.Api.Enable || !coreConfig.Api.EnableDashboard)
            return "Dashboard 未启用：在配置中开启 api.enable 与 api.enable_dashboard 后重启宿主。";

        var url = coreConfig.Api.GetDashboardLoginUrl();
        try
        {
            // The shell opens URLs with the default browser on Windows, macOS (open) and Linux (xdg-open).
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return "已在浏览器中打开 Dashboard。";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // No browser here (a server or container): the link still signs in from another machine's browser.
            return "无法打开浏览器，请手动访问（链接含 API 密钥，勿外传）:\n" + url;
        }
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
