using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed class HostCommandHandler(
    BotContext botContext,
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
        new("load", "热加载指定插件"),
        new("unload", "热卸载指定插件"),
        new("restart", "重启程序"),
        new("/api", "显示或设置 API 鉴权信息"),
        new("update", "查看或处理待确认更新"),
        new("path", "打开当前程序目录"),
        new("log", "切换日志输出"),
        new("clear", "清除控制台"),
        new("exit", "退出程序"),
        new("quit", "退出程序")
    ];

    public void RunConsoleLoop(TaskCompletionSource<bool> exitRequested)
    {
        while (true)
        {
            var input = CH.ReadPrompt(
                "> ",
                () => BuildConsoleCompletions(
                    pluginManager.GetLoadedPluginNames(),
                    pluginManager.GetLoadablePluginCandidates(includePluginNames: false)));
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
                        if (TryStartReplacementProcess())
                        {
                            exitRequested.TrySetResult(true);
                            return;
                        }

                        break;
                    case "api":
                        CH.Info(HandleApiCommand(splitInput));
                        break;
                    case "help":
                        var orderedCommands = ConsoleCommands
                            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var nameWidth = Math.Max(orderedCommands.Max(command => command.Name.Length), 8) + 2;
                        var helpText = new StringBuilder()
                            .AppendLine("可用命令")
                            .AppendLine(new string('-', 24));

                        foreach (var command in orderedCommands)
                            helpText.Append("  ")
                                .Append(command.Name.PadRight(nameWidth))
                                .AppendLine(command.Description);

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

    public async Task HandleFriendMessageAsync(FriendIncomingMessage message)
    {
        if (await TryHandleHostPrivateCommandAsync(message))
            return;
        await eventDispatcher.PublishAsync(message);
    }

    private async Task<bool> TryHandleHostPrivateCommandAsync(FriendIncomingMessage message)
    {
        if (!botContext.OwnerList.Contains(message.SenderId)) return false;

        var input = message.GetPlainText().Trim();
        if (string.IsNullOrWhiteSpace(input)) return false;

        var splitInput = input.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
        if (splitInput.Length == 0) return false;

        switch (NormalizeCommand(splitInput[0]))
        {
            case "help":
            {
                var orderedCommands = ConsoleCommands
                    .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var nameWidth = Math.Max(orderedCommands.Max(command => command.Name.Length), 8) + 2;
                var helpText = new StringBuilder()
                    .AppendLine("可用命令")
                    .AppendLine(new string('-', 24));

                foreach (var command in orderedCommands)
                    helpText.Append("  ")
                        .Append(command.Name.PadRight(nameWidth))
                        .AppendLine(command.Description);

                await botContext.Message.ReplyAsync(message, helpText.ToString().TrimEnd());
                return true;
            }
            case "plugins":
            {
                await botContext.Message.ReplyAsync(
                    message,
                    BuildLoadedPluginsText(pluginManager.GetLoadedPluginSnapshot()));
                return true;
            }
            case "update":
            {
                if (splitInput.Length < 2)
                {
                    var pending = Updater.GetPendingUpdates();
                    await botContext.Message.ReplyAsync(
                        message,
                        pending.Count == 0
                            ? "当前没有待确认的更新请求。"
                            : string.Join("\n", pending.Select(item =>
                                $"{item.Id} | {item.Target} | {item.Name} {item.CurrentVersion} -> {item.LatestVersion}")));
                    return true;
                }

                switch (splitInput[1].ToLowerInvariant())
                {
                    case "list":
                    {
                        var pending = Updater.GetPendingUpdates();
                        await botContext.Message.ReplyAsync(
                            message,
                            pending.Count == 0
                                ? "当前没有待确认的更新请求。"
                                : string.Join("\n", pending.Select(item =>
                                    $"{item.Id} | {item.Target} | {item.Name} {item.CurrentVersion} -> {item.LatestVersion}")));
                        return true;
                    }
                    case "confirm":
                    {
                        if (splitInput.Length < 3)
                        {
                            await botContext.Message.ReplyAsync(message, "用法: update confirm <id>");
                            return true;
                        }

                        var ok = await Updater.ConfirmUpdateAsync(splitInput[2]);
                        await botContext.Message.ReplyAsync(message, ok ? "已开始执行更新。" : "未找到该更新请求。");
                        return true;
                    }
                    case "cancel":
                    {
                        if (splitInput.Length < 3)
                        {
                            await botContext.Message.ReplyAsync(message, "用法: update cancel <id>");
                            return true;
                        }

                        var ok = Updater.CancelUpdate(splitInput[2]);
                        await botContext.Message.ReplyAsync(message, ok ? "已取消更新请求。" : "未找到该更新请求。");
                        return true;
                    }
                    default:
                        await botContext.Message.ReplyAsync(message, "用法: update [list|confirm|cancel]");
                        return true;
                }
            }
            case "api":
            {
                await botContext.Message.ReplyAsync(message, HandleApiCommand(splitInput));
                return true;
            }
            case "load":
            {
                if (splitInput.Length < 2)
                {
                    await botContext.Message.ReplyAsync(message, "用法: load <插件名|dll路径>");
                    return true;
                }

                await pluginManager.ScheduleLoadPluginByName(
                    eventDispatcher,
                    routePolicy,
                    splitInput[1]);
                await botContext.Message.ReplyAsync(message, $"已加入热加载队列: {splitInput[1]}");
                return true;
            }
            case "unload":
            {
                if (splitInput.Length < 2)
                {
                    await botContext.Message.ReplyAsync(message, "用法: unload <插件名>");
                    return true;
                }

                await pluginManager.ScheduleUnloadPluginByName(
                    eventDispatcher,
                    splitInput[1]);
                await botContext.Message.ReplyAsync(message, $"已加入热卸载队列: {splitInput[1]}");
                return true;
            }
            default:
                return false;
        }
    }

    private string HandleApiCommand(string[] splitInput)
    {
        if (splitInput.Length == 1)
        {
            return BuildApiInfoText();
        }

        if (!string.Equals(splitInput[1], "token", StringComparison.OrdinalIgnoreCase))
        {
            return "用法: /api | /api token | /api token <密钥>";
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
            ? (coreConfig.Api.ListenUrls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)) ?? coreConfig.Api.ListenUrl)
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

    private static IReadOnlyList<CH.ConsoleCommandOption> BuildConsoleCompletions(
        IReadOnlyList<string> loadedPluginNames,
        IReadOnlyList<string> loadablePluginCandidates)
    {
        var completions = new List<ConsoleHelper.ConsoleCommandOption>(ConsoleCommands);
        var loadedNameSet = new HashSet<string>(loadedPluginNames, StringComparer.OrdinalIgnoreCase);

        foreach (var pluginName in loadedPluginNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            completions.Add(
                new ConsoleHelper.ConsoleCommandOption($"unload {pluginName}", $"热卸载插件 {pluginName}"));
        }

        foreach (var candidate in loadablePluginCandidates.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (IsAlreadyLoadedCandidate(candidate, loadedNameSet)) continue;

            completions.Add(new ConsoleHelper.ConsoleCommandOption($"load {candidate}", $"热加载插件 {candidate}"));
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
            .AppendLine("名称 | 版本 | 程序集占用");
        long totalBytes = 0;

        foreach (var plugin in orderedPlugins)
        {
            var bytes = plugin.GetLoadedAssemblyBytes();
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
            .Append(FormatBytes(totalBytes));

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

    private static bool TryStartReplacementProcess()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            CH.Error("无法确定当前进程路径，重启失败。");
            return false;
        }

        try
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                Arguments = string.Join(" ", args.Select(QuoteArgument))
            });
            CH.Info("已启动新进程，当前进程即将退出。");
            return true;
        }
        catch (Exception ex)
        {
            CH.Error("重启失败: " + ex.Message);
            return false;
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0) return "\"\"";

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : argument;
    }
}
