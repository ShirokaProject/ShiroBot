using System.Diagnostics;
using System.Text;
using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed class HostCommandHandler
{
    private static readonly IReadOnlyList<CH.ConsoleCommandOption> ConsoleCommands =
    [
        new("help", "显示帮助信息"),
        new("plugins", "显示已加载插件"),
        new("load-plugin", "热加载指定插件"),
        new("unload-plugin", "热卸载指定插件"),
        new("update", "查看或处理待确认更新"),
        new("path", "打开当前程序目录"),
        new("log", "切换日志输出"),
        new("clear", "清除控制台"),
        new("unload", "卸载并退出"),
        new("exit", "退出程序"),
        new("quit", "退出程序")
    ];

    private readonly BotContext _botContext;
    private readonly PluginManager _pluginManager;
    private readonly HostEventDispatcher _eventDispatcher;
    private readonly PluginRouteConfig _routePolicy;

    public HostCommandHandler(
        BotContext botContext,
        PluginManager pluginManager,
        HostEventDispatcher eventDispatcher,
        PluginRouteConfig routePolicy)
    {
        _botContext = botContext;
        _pluginManager = pluginManager;
        _eventDispatcher = eventDispatcher;
        _routePolicy = routePolicy;
    }

    public void RunConsoleLoop(TaskCompletionSource<bool> exitRequested)
    {
        while (true)
        {
            var input = CH.ReadPrompt("> ", BuildConsoleCompletions(_pluginManager.GetLoadedPluginNames()));
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (CH.IsEnabled ||
                input.StartsWith("log", StringComparison.CurrentCultureIgnoreCase))
            {
                var splitInput = input.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
                switch (splitInput.FirstOrDefault()?.ToLowerInvariant())
                {
                    case "unload":
                    case "exit":
                    case "quit":
                        exitRequested.TrySetResult(true);
                        return;
                    case "plugins":
                        CH.Info(BuildLoadedPluginsText(_pluginManager.GetLoadedPluginSnapshot()));
                        break;
                    case "load-plugin":
                        if (splitInput.Length < 2)
                        {
                            CH.Warning("用法: load-plugin <插件名|dll路径>");
                            break;
                        }

                        _pluginManager.ScheduleLoadPluginByName(
                            _eventDispatcher,
                            _routePolicy,
                            splitInput[1]).GetAwaiter().GetResult();
                        break;
                    case "unload-plugin":
                        if (splitInput.Length < 2)
                        {
                            CH.Warning("用法: unload-plugin <插件名>");
                            break;
                        }

                        _pluginManager.ScheduleUnloadPluginByName(
                            _eventDispatcher,
                            splitInput[1]).GetAwaiter().GetResult();
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
        await _eventDispatcher.PublishAsync(message);
    }

    private async Task<bool> TryHandleHostPrivateCommandAsync(FriendIncomingMessage message)
    {
        if (!_botContext.OwnerList.Contains(message.SenderId)) return false;

        var input = message.GetPlainText().Trim();
        if (string.IsNullOrWhiteSpace(input)) return false;

        var splitInput = input.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
        if (splitInput.Length == 0) return false;

        switch (splitInput[0].ToLowerInvariant())
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

                await _botContext.Message.ReplyAsync(message, helpText.ToString().TrimEnd());
                return true;
            }
            case "plugins":
            {
                await _botContext.Message.ReplyAsync(
                    message,
                    BuildLoadedPluginsText(_pluginManager.GetLoadedPluginSnapshot()));
                return true;
            }
            case "update":
            {
                if (splitInput.Length < 2)
                {
                    var pending = Core.Updater.GetPendingUpdates();
                    await _botContext.Message.ReplyAsync(
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
                        var pending = Core.Updater.GetPendingUpdates();
                        await _botContext.Message.ReplyAsync(
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
                            await _botContext.Message.ReplyAsync(message, "用法: update confirm <id>");
                            return true;
                        }

                        var ok = await Core.Updater.ConfirmUpdateAsync(splitInput[2]);
                        await _botContext.Message.ReplyAsync(message, ok ? "已开始执行更新。" : "未找到该更新请求。");
                        return true;
                    }
                    case "cancel":
                    {
                        if (splitInput.Length < 3)
                        {
                            await _botContext.Message.ReplyAsync(message, "用法: update cancel <id>");
                            return true;
                        }

                        var ok = Core.Updater.CancelUpdate(splitInput[2]);
                        await _botContext.Message.ReplyAsync(message, ok ? "已取消更新请求。" : "未找到该更新请求。");
                        return true;
                    }
                    default:
                        await _botContext.Message.ReplyAsync(message, "用法: update [list|confirm|cancel]");
                        return true;
                }
            }
            case "load-plugin":
            {
                if (splitInput.Length < 2)
                {
                    await _botContext.Message.ReplyAsync(message, "用法: load-plugin <插件名|dll路径>");
                    return true;
                }

                await _pluginManager.ScheduleLoadPluginByName(
                    _eventDispatcher,
                    _routePolicy,
                    splitInput[1]);
                await _botContext.Message.ReplyAsync(message, $"已加入热加载队列: {splitInput[1]}");
                return true;
            }
            case "unload-plugin":
            {
                if (splitInput.Length < 2)
                {
                    await _botContext.Message.ReplyAsync(message, "用法: unload-plugin <插件名>");
                    return true;
                }

                await _pluginManager.ScheduleUnloadPluginByName(
                    _eventDispatcher,
                    splitInput[1]);
                await _botContext.Message.ReplyAsync(message, $"已加入热卸载队列: {splitInput[1]}");
                return true;
            }
            default:
                return false;
        }
    }

    private static IReadOnlyList<CH.ConsoleCommandOption> BuildConsoleCompletions(
        IReadOnlyList<string> loadedPluginNames)
    {
        var completions = new List<ConsoleHelper.ConsoleCommandOption>(ConsoleCommands);

        foreach (var pluginName in loadedPluginNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            completions.Add(
                new ConsoleHelper.ConsoleCommandOption($"unload-plugin {pluginName}", $"热卸载插件 {pluginName}"));
            completions.Add(new ConsoleHelper.ConsoleCommandOption($"load-plugin {pluginName}", $"热加载插件 {pluginName}"));
        }

        return completions;
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

    private static bool CanReadInteractiveKey()
    {
        return Environment.UserInteractive &&
               !Console.IsInputRedirected &&
               !Console.IsOutputRedirected;
    }
}
