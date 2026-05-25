using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using ShiroBot.Core;
using ShiroBot.Hosting;
using ShiroBot.Hosting.Context;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot;

public static class Program
{
    private static readonly MethodInfo CreateAdapterEventBridgeHandlerMethod =
        typeof(Program).GetMethod(nameof(CreateAdapterEventBridgeHandler), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Failed to locate {nameof(CreateAdapterEventBridgeHandler)}.");

    private static readonly MethodInfo PublishAdapterEventAsyncMethod =
        typeof(Program).GetMethod(nameof(PublishAdapterEventAsync), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Failed to locate {nameof(PublishAdapterEventAsync)}.");

    private static string _pluginRootPath = Path.Combine(BasePath, "plugins");
    private static readonly Lock PluginLifecycleLock = new();
    private static readonly SemaphoreSlim PluginUnloadSemaphore = new(1, 1);
    private static List<LoadedPluginHandle> _loadedPlugins = new();
    private static readonly List<Task> PluginBackgroundTasks = [];
    private static bool _isShuttingDown;
    private static IDisposable? _coreConfigWatcher;

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

    private static string BasePath => AppContext.BaseDirectory;

    private static BotContext Context { get; set; } = null!;

    public static async Task Main(string[] args)
    {
        BotLog.SetDefault(new ConsoleLogger());

        var configOption = new Option<string?>("--config", "-c")
        {
            Description = "指定配置文件路径"
        };

        var adapterOption = new Option<string?>("--adapter")
        {
            Description = "指定适配器 DLL 路径"
        };

        var pluginOption = new Option<string?>("--plugin-dir")
        {
            Description = "指定插件文件夹路径（可以是相对路径或绝对路径）"
        };

        var noConsoleOption = new Option<bool>("--no-console")
        {
            Description = "禁用控制台交互输入"
        };

        var rootCommand = new RootCommand("ShiroBot 主程序")
        {
            adapterOption,
            pluginOption,
            configOption,
            noConsoleOption
        };

        var parserResult = rootCommand.Parse(args);

        CH.Info("ShiroBot 启动中...");

        lock (PluginLifecycleLock)
        {
            _loadedPlugins = [];
            PluginBackgroundTasks.Clear();
            _isShuttingDown = false;
        }

        try
        {
            //get coreConfig path
            var coreConfigPath = Path.Combine(AppContext.BaseDirectory, "config.toml");
            //if command line config option is set, override coreConfig path
            var configuredCoreConfigPath = parserResult.GetValue(configOption);
            if (!string.IsNullOrWhiteSpace(configuredCoreConfigPath))
            {
                BotLog.Info("检测到命令行配置路径，使用指定的配置文件: " + configuredCoreConfigPath);
                if (!File.Exists(configuredCoreConfigPath))
                {
                    BotLog.Error("指定的配置文件不存在: " + configuredCoreConfigPath);
                    return;
                }

                coreConfigPath = configuredCoreConfigPath;
            }
            else
            {
                BotLog.Info("加载核心配置文件: " + coreConfigPath);
            }

            //load coreConfig
            var coreConfigManager = new ConfigManager(coreConfigPath);
            var coreConfig = await coreConfigManager.LoadCoreConfig();

            //set coreConfig to global context
            CH.IsEnabled = coreConfig.EnableLog;
            var groupRoutePolicy = coreConfig.PluginRoutes;

            //load adapter : from config
            var adapterRoot = Path.Combine(AppContext.BaseDirectory, "adapters");
            if (!Directory.Exists(adapterRoot))
            {
                BotLog.Info("适配器目录不存在，创建目录: " + adapterRoot);
                Directory.CreateDirectory(adapterRoot);
            }

            var adapterName = coreConfig.Protocol.EndsWith("dll", StringComparison.OrdinalIgnoreCase)
                ? coreConfig.Protocol[..^4]
                : coreConfig.Protocol;
            var adapterPath = Path.Combine(BasePath, "adapters", $"{adapterName}.dll");
            //load adapter: from config : check if adapters in adapters folder
            if (!File.Exists(adapterPath) &&
                File.Exists(Path.Combine(BasePath, "adapters", adapterName, $"{adapterName}.dll")))
                adapterPath = Path.Combine(BasePath, "adapters", adapterName, $"{adapterName}.dll");

            //load adapter: use command line adapter path if specified and exists
            var commandAdapterPath = parserResult.GetValue(adapterOption);
            if (!string.IsNullOrWhiteSpace(commandAdapterPath))
            {
                BotLog.Info("检测到命令行适配器路径，使用指定的适配器文件: " + commandAdapterPath);
                adapterPath = commandAdapterPath;
            }

            //load adapter: final fallback
            if (!File.Exists(adapterPath))
            {
                //fall back to singleDll
                var fallbackDll = Directory.EnumerateFiles(Path.Combine(BasePath, "adapters"),
                    "*.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (fallbackDll is not null)
                    adapterPath = fallbackDll;
                else
                    fallbackDll = Directory
                        .EnumerateDirectories(Path.Combine(BasePath, "adapters"))
                        .SelectMany(folder => Directory.EnumerateFiles(folder,
                            "*.dll", SearchOption.TopDirectoryOnly))
                        .FirstOrDefault();
                BotLog.Warning(fallbackDll is not null
                    ? $"未配置适配器，自动选择适配器: {fallbackDll}"
                    : "未找到任何适配器文件，请确认 adapters 目录下存在适配器 DLL 文件。");
            }

            if (!File.Exists(adapterPath))
            {
                CH.Warning("请确认 adapters 目录下存在对应的适配器文件，或在 config.toml 中配置 protocol...");
                if (CanReadInteractiveKey()) Console.ReadKey();
                return;
            }

            CH.Log("开始加载适配器: " + adapterPath);
            var adapterLoader = new DllLoader<IBotAdapter>();
            var adapter = adapterLoader.Load(adapterPath);
            var adapterConfigPath = ResolveAdapterConfigPath(adapterRoot, adapterPath);
            adapter.Config = ConfigContext.ForAdapter(adapterConfigPath);
            adapter.Logger = new ConsoleLogger($"[Adapter:{adapter.Name}]");

            var adapterMetadata = adapter.Metadata;
            CH.Log($"适配器信息: {adapterMetadata.Name} v{adapterMetadata.Version}");

            using (BotLog.BeginScope(adapter.Logger))
            {
                await adapter.StartAsync();
            }

            CH.Success("加载适配器成功: " + adapter.Name);

            Context = new BotContext(adapter, coreConfig.OwnerList, coreConfig.AdminList);
            Core.Updater.Initialize(
                () => Context.OwnerList,
                (ownerId, content) => Context.Message.SendPrivateMessageAsync(ownerId, content));
            var hostEventDispatcher = new HostEventDispatcher(PluginLifecycleLock);

            // 核心配置热重载：监听 config.toml，动态把变化应用到 owners/admins/插件路由/日志开关。
            _coreConfigWatcher = ConfigContext.ForCore(coreConfigPath).Watch<CoreConfig>(updated =>
            {
                ApplyCoreConfigChange(coreConfig, updated);
            });

            var commandPluginDirectory = parserResult.GetValue(pluginOption);
            if (!string.IsNullOrWhiteSpace(commandPluginDirectory))
            {
                BotLog.Info("检测到指定插件目录: " + commandPluginDirectory);
                if (!Directory.Exists(commandPluginDirectory))
                    BotLog.Info("指定插件目录不存在，回退到默认目录: " + _pluginRootPath);
                else
                    _pluginRootPath = commandPluginDirectory;
            }

            if (!Directory.Exists(_pluginRootPath))
            {
                BotLog.Info("插件目录不存在，创建目录: " + _pluginRootPath);
                Directory.CreateDirectory(_pluginRootPath);
            }

            BridgeAdapterEvents(
                adapter.Event,
                hostEventDispatcher,
                _pluginRootPath,
                groupRoutePolicy);

            CH.Info("开始加载插件...");

            await LoadPluginsAsync(
                EnumeratePluginEntryAssemblies(_pluginRootPath).ToList(),
                hostEventDispatcher,
                groupRoutePolicy);

            CH.Success("已加载插件: " + string.Join(", ", GetLoadedPluginNames()));

            //check if console is available or disabled by config or command line
            var configuredConsoleOption = parserResult.GetValue(noConsoleOption);
            var hasConsole =
                Environment.UserInteractive &&
                !Console.IsInputRedirected &&
                !Console.IsOutputRedirected;
            var enableConsoleInput = hasConsole && !configuredConsoleOption && !coreConfig.DisableConsoleInput;

            switch (enableConsoleInput)
            {
                case false:
                {
                    var reasons = new List<string>();

                    if (!hasConsole) reasons.Add("检测到非交互终端");

                    if (configuredConsoleOption) reasons.Add("命令行参数 --no-console 已启用");

                    if (coreConfig.DisableConsoleInput) reasons.Add("配置项 disable_console_input = true");

                    CH.Info($"已禁用控制台命令输入: {string.Join("，", reasons)}");
                    break;
                }
                case true:
                {
                    var exitRequested =
                        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ = Task.Run(() => RunConsoleCommandLoop(
                        exitRequested,
                        GetLoadedPluginNames,
                        pluginName => ScheduleLoadPluginByName(
                            _pluginRootPath,
                            hostEventDispatcher,
                            groupRoutePolicy,
                            pluginName),
                        pluginName => ScheduleUnloadPluginByName(
                            hostEventDispatcher,
                            pluginName)));
                    await exitRequested.Task;
                    return;
                }
            }

            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            CH.Error("程序启动失败: " + ex.Message);
            CH.Warning("按任意键退出...");
            if (CanReadInteractiveKey()) Console.ReadKey();
        }
        finally
        {
            try
            {
                _coreConfigWatcher?.Dispose();
            }
            catch (Exception ex)
            {
                CH.Warning("释放核心配置监听器时出现异常: " + ex.Message);
            }
            finally
            {
                _coreConfigWatcher = null;
            }

            await AwaitPluginBackgroundTasksAsync();

            foreach (var pluginHandle in Enumerable.Reverse(GetLoadedPluginSnapshot()))
            {
                var result = await pluginHandle.UnloadAsync();
                if (result.Error is not null) CH.Error($"插件卸载失败: {result.Name} - {result.Error.Message}");

                var assemblyUnloaded = DllLoader<IBotPlugin>.WaitForUnload(result.AssemblyLoadContextWeakReference);
                if (!assemblyUnloaded) CH.Warning($"插件程序集未完全卸载，可能仍有引用残留: {result.Name} ({result.AssemblyPath})");
            }
        }
    }

    /*
     private method
     */
    private static bool CanReadInteractiveKey()
    {
        return Environment.UserInteractive &&
               !Console.IsInputRedirected &&
               !Console.IsOutputRedirected;
    }

    /// <summary>
    /// 把磁盘上重新加载到的核心配置应用到运行中的宿主：
    /// 同步 owners/admins、插件路由策略、日志开关，并就地修改 <paramref name="active"/> 让现有引用立即看到新值。
    /// 不会替换 adapter / 重新加载插件，避免一次配置编辑导致整个连接重启。
    /// </summary>
    private static void ApplyCoreConfigChange(CoreConfig active, CoreConfig updated)
    {
        if (active is null || updated is null) return;

        var changes = new List<string>();

        if (!ArrayEquals(active.OwnerList, updated.OwnerList))
        {
            active.OwnerList = updated.OwnerList;
            Context?.UpdateOwnerList(updated.OwnerList);
            changes.Add($"owners[{updated.OwnerList.Length}]");
        }

        if (!ArrayEquals(active.AdminList, updated.AdminList))
        {
            active.AdminList = updated.AdminList;
            Context?.UpdateAdminList(updated.AdminList);
            changes.Add($"admins[{updated.AdminList.Length}]");
        }

        if (active.EnableLog != updated.EnableLog)
        {
            active.EnableLog = updated.EnableLog;
            CH.IsEnabled = updated.EnableLog;
            changes.Add($"enable_log={updated.EnableLog}");
        }

        if (active.DisableConsoleInput != updated.DisableConsoleInput)
        {
            // 控制台输入循环是启动期一次性决定的，运行期改这个值不会即时生效。
            active.DisableConsoleInput = updated.DisableConsoleInput;
            changes.Add("disable_console_input(下次启动生效)");
        }

        if (!string.Equals(active.Protocol, updated.Protocol, StringComparison.OrdinalIgnoreCase))
        {
            active.Protocol = updated.Protocol;
            changes.Add("protocol(下次启动生效)");
        }

        if (updated.PluginRoutes is not null)
        {
            active.PluginRoutes.CopyFrom(updated.PluginRoutes);
            changes.Add("plugin_routes");
        }

        if (changes.Count > 0)
        {
            CH.Success("核心配置热重载完成: " + string.Join(", ", changes));
        }
        else
        {
            CH.Info("核心配置热重载: 无字段变化。");
        }
    }

    private static bool ArrayEquals(long[]? left, long[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    private static string ResolveAdapterConfigPath(string adapterRoot, string adapterPath)
    {
        var normalizedAdapterRoot = Path.GetFullPath(adapterRoot).TrimEnd(Path.DirectorySeparatorChar);
        var parentDirectory = Path.GetDirectoryName(adapterPath) ?? adapterRoot;
        var normalizedParent = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var folderName = new DirectoryInfo(parentDirectory).Name;
        var fileName = Path.GetFileNameWithoutExtension(adapterPath);
        var isFolderBasedAdapter =
            !string.Equals(normalizedParent, normalizedAdapterRoot, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(folderName, fileName, StringComparison.OrdinalIgnoreCase);

        return !isFolderBasedAdapter
            ? Path.ChangeExtension(adapterPath, ".toml")
            : Path.Combine(normalizedParent, "config.toml");
    }

    private static void RunConsoleCommandLoop(
        TaskCompletionSource<bool> exitRequested,
        Func<IReadOnlyList<string>> getLoadedPluginNames,
        Func<string, Task> loadPluginByName,
        Func<string, Task> unloadPluginByName)
    {
        while (true)
        {
            var input = CH.ReadPrompt("> ", BuildConsoleCompletions(getLoadedPluginNames()));
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
                        var pluginNames = getLoadedPluginNames();
                        CH.Info(pluginNames.Count == 0
                            ? "当前没有已加载插件。"
                            : "已加载插件: " + string.Join(", ", pluginNames));
                        break;
                    case "load-plugin":
                        if (splitInput.Length < 2)
                        {
                            CH.Warning("用法: load-plugin <插件名|dll路径>");
                            break;
                        }

                        loadPluginByName(splitInput[1]).GetAwaiter().GetResult();
                        break;
                    case "unload-plugin":
                        if (splitInput.Length < 2)
                        {
                            CH.Warning("用法: unload-plugin <插件名>");
                            break;
                        }

                        unloadPluginByName(splitInput[1]).GetAwaiter().GetResult();
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

    private static IEnumerable<string> EnumeratePluginEntryAssemblies(string pluginRoot)
    {
        var sharedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShiroBot.SDK.dll",
            "ShiroBot.Model.dll"
        };
        var yieldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rootLevelDlls = Directory.EnumerateFiles(pluginRoot, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(dll => !sharedAssemblies.Contains(Path.GetFileName(dll)))
            .OrderBy(dll => dll, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var normalizedPath in rootLevelDlls.Select(Path.GetFullPath).Where(yieldedPaths.Add))
            yield return normalizedPath;

        var pluginDirectories = Directory.EnumerateDirectories(pluginRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var normalizedPath in from directory in pluginDirectories
                 let directoryName = new DirectoryInfo(directory).Name
                 select Path.Combine(directory, $"{directoryName}.dll")
                 into entryDll
                 where File.Exists(entryDll) && !sharedAssemblies.Contains(Path.GetFileName(entryDll))
                 select Path.GetFullPath(entryDll)
                 into normalizedPath
                 where yieldedPaths.Add(normalizedPath)
                 select normalizedPath) yield return normalizedPath;
    }

    private static List<string> GetLoadedPluginNames()
    {
        lock (PluginLifecycleLock)
        {
            return _loadedPlugins
                .Select(plugin => plugin.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static List<LoadedPluginHandle> GetLoadedPluginSnapshot()
    {
        lock (PluginLifecycleLock)
        {
            return _loadedPlugins.ToList();
        }
    }

    private static Task[] BeginShutdownAndGetBackgroundTasks()
    {
        lock (PluginLifecycleLock)
        {
            _isShuttingDown = true;
            return PluginBackgroundTasks.ToArray();
        }
    }

    private static async Task AwaitPluginBackgroundTasksAsync()
    {
        var tasks = BeginShutdownAndGetBackgroundTasks();
        if (tasks.Length == 0) return;

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            CH.Warning("等待插件后台任务收敛时出现异常: " + ex.Message);
        }
    }

    private static Task? TryQueuePluginBackgroundTask(Func<Task> taskFactory)
    {
        Task? task;
        lock (PluginLifecycleLock)
        {
            if (_isShuttingDown) return null;

            task = Task.Run(taskFactory);
            PluginBackgroundTasks.Add(task);
        }

        _ = task.ContinueWith(
            _ =>
            {
                lock (PluginLifecycleLock)
                {
                    PluginBackgroundTasks.Remove(task);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    private static void BridgeAdapterEvents(
        IEventService eventService,
        HostEventDispatcher eventSink,
        string pluginRoot,
        PluginRouteConfig routePolicy)
    {
        foreach (var eventInfo in typeof(IEventService).GetEvents())
        {
            var payloadType = GetAdapterEventPayloadType(eventInfo);
            var handler = CreateAdapterEventBridgeDelegate(
                eventInfo,
                payloadType,
                eventSink,
                pluginRoot,
                routePolicy);

            eventInfo.AddEventHandler(eventService, handler);
        }
    }

    private static Delegate CreateAdapterEventBridgeDelegate(
        EventInfo eventInfo,
        Type payloadType,
        HostEventDispatcher eventSink,
        string pluginRoot,
        PluginRouteConfig routePolicy)
    {
        var eventName = GetAdapterEventDisplayName(eventInfo.Name, payloadType);

        if (payloadType == typeof(FriendIncomingMessage))
            return CreateAdapterEventBridgeHandler<FriendIncomingMessage>(
                message => HandleFriendMessageAsync(
                    message,
                    eventSink,
                    pluginRoot,
                    routePolicy),
                eventName);

        if (!typeof(Event).IsAssignableFrom(payloadType))
            throw new InvalidOperationException(
                $"Adapter event '{eventInfo.Name}' payload '{payloadType.Name}' does not implement '{nameof(Event)}'.");

        var factory = CreateAdapterEventBridgeHandlerMethod.MakeGenericMethod(payloadType);
        return (Delegate)factory.Invoke(null, [CreateEventPublisher(eventSink, payloadType), eventName])!;
    }

    private static Type GetAdapterEventPayloadType(EventInfo eventInfo)
    {
        var invokeMethod = eventInfo.EventHandlerType?.GetMethod("Invoke");
        if (invokeMethod is null)
            throw new InvalidOperationException(
                $"Adapter event '{eventInfo.Name}' does not expose an invokable handler type.");

        var parameters = invokeMethod.GetParameters();
        if (invokeMethod.ReturnType != typeof(Task) || parameters.Length != 1)
            throw new InvalidOperationException(
                $"Adapter event '{eventInfo.Name}' must use a handler shaped like Func<TEvent, Task>.");

        return parameters[0].ParameterType;
    }

    private static Func<TEvent, Task> CreateAdapterEventBridgeHandler<TEvent>(Func<TEvent, Task> dispatcher,
        string eventName)
    {
        return message => DispatchAdapterEventInBackground(() => dispatcher(message), eventName);
    }

    private static object CreateEventPublisher(HostEventDispatcher eventSink, Type payloadType)
    {
        var publisherType = typeof(Func<,>).MakeGenericType(payloadType, typeof(Task));
        var publishMethod = PublishAdapterEventAsyncMethod.MakeGenericMethod(payloadType);

        return Delegate.CreateDelegate(publisherType, eventSink, publishMethod);
    }

    private static Task PublishAdapterEventAsync<TEvent>(HostEventDispatcher eventSink, TEvent message)
        where TEvent : Event
    {
        return eventSink.PublishAsync(message);
    }

    private static string GetAdapterEventDisplayName(string eventName, Type payloadType)
    {
        if (payloadType == typeof(GroupIncomingMessage)) return "群消息";

        if (payloadType == typeof(FriendIncomingMessage)) return "好友消息";

        return payloadType.Name switch
        {
            nameof(MessageRecallEvent) => "消息撤回",
            nameof(FriendRequestEvent) => "好友请求",
            nameof(GroupJoinRequestEvent) => "入群请求",
            nameof(GroupInvitedJoinRequestEvent) => "群成员邀请他人入群请求",
            nameof(GroupInvitationEvent) => "他人邀请自身入群",
            nameof(FriendNudgeEvent) => "好友戳一戳",
            nameof(FriendFileUploadEvent) => "好友文件上传",
            nameof(GroupAdminChangeEvent) => "群管理员变更",
            nameof(GroupEssenceMessageChangeEvent) => "群精华消息变更",
            nameof(GroupMemberIncreaseEvent) => "群成员增加",
            nameof(GroupMemberDecreaseEvent) => "群成员减少",
            nameof(GroupNameChangeEvent) => "群名称变更",
            nameof(GroupMessageReactionEvent) => "群消息表情回应",
            nameof(GroupMuteEvent) => "群禁言",
            nameof(GroupWholeMuteEvent) => "群全体禁言",
            nameof(GroupNudgeEvent) => "群戳一戳",
            nameof(GroupFileUploadEvent) => "群文件上传",
            _ => eventName
        };
    }

    private static Task DispatchAdapterEventInBackground(Func<Task> handler, string eventName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                CH.Error($"适配器事件后台分发失败: {eventName} - {ex.Message}");
            }
        });

        return Task.CompletedTask;
    }

    private static async Task HandleFriendMessageAsync(
        FriendIncomingMessage message,
        HostEventDispatcher eventSink,
        string pluginRoot,
        PluginRouteConfig routePolicy)
    {
        if (await TryHandleHostPrivateCommandAsync(
                message,
                pluginRoot,
                routePolicy,
                eventSink))
            return;
        await eventSink.PublishAsync(message);
    }

    private static async Task<bool> TryHandleHostPrivateCommandAsync(
        FriendIncomingMessage message,
        string pluginRoot,
        PluginRouteConfig routePolicy,
        HostEventDispatcher hostEventDispatcher)
    {
        if (!Context.OwnerList.Contains(message.SenderId)) return false;

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

                await Context.Message.ReplyAsync(message, helpText.ToString().TrimEnd());
                return true;
            }
            case "plugins":
            {
                var pluginNames = GetLoadedPluginNames();
                await Context.Message.ReplyAsync(
                    message,
                    pluginNames.Count == 0
                        ? "当前没有已加载插件。"
                        : "已加载插件: " + string.Join(", ", pluginNames));
                return true;
            }
            case "update":
            {
                if (splitInput.Length < 2)
                {
                    var pending = Core.Updater.GetPendingUpdates();
                    await Context.Message.ReplyAsync(
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
                        await Context.Message.ReplyAsync(
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
                            await Context.Message.ReplyAsync(message, "用法: update confirm <id>");
                            return true;
                        }

                        var ok = await Core.Updater.ConfirmUpdateAsync(splitInput[2]);
                        await Context.Message.ReplyAsync(message, ok ? "已开始执行更新。" : "未找到该更新请求。");
                        return true;
                    }
                    case "cancel":
                    {
                        if (splitInput.Length < 3)
                        {
                            await Context.Message.ReplyAsync(message, "用法: update cancel <id>");
                            return true;
                        }

                        var ok = Core.Updater.CancelUpdate(splitInput[2]);
                        await Context.Message.ReplyAsync(message, ok ? "已取消更新请求。" : "未找到该更新请求。");
                        return true;
                    }
                    default:
                        await Context.Message.ReplyAsync(message, "用法: update [list|confirm|cancel]");
                        return true;
                }
            }
            case "load-plugin":
            {
                if (splitInput.Length < 2)
                {
                    await Context.Message.ReplyAsync(message, "用法: load-plugin <插件名|dll路径>");
                    return true;
                }

                await ScheduleLoadPluginByName(
                    pluginRoot,
                    hostEventDispatcher,
                    routePolicy,
                    splitInput[1]);
                await Context.Message.ReplyAsync(message, $"已加入热加载队列: {splitInput[1]}");
                return true;
            }
            case "unload-plugin":
            {
                if (splitInput.Length < 2)
                {
                    await Context.Message.ReplyAsync(message, "用法: unload-plugin <插件名>");
                    return true;
                }

                await ScheduleUnloadPluginByName(
                    hostEventDispatcher,
                    splitInput[1]);
                await Context.Message.ReplyAsync(message, $"已加入热卸载队列: {splitInput[1]}");
                return true;
            }
            default:
                return false;
        }
    }

    private static Task ScheduleUnloadPluginByName(
        HostEventDispatcher hostEventDispatcher,
        string pluginName)
    {
        lock (PluginLifecycleLock)
        {
            if (_isShuttingDown)
            {
                CH.Warning($"程序正在退出，忽略热卸载请求: {pluginName}");
                return Task.CompletedTask;
            }
        }

        LoadedPluginHandle? pluginHandle;
        lock (PluginLifecycleLock)
        {
            pluginHandle = _loadedPlugins.FirstOrDefault(plugin =>
                string.Equals(plugin.Name, pluginName, StringComparison.OrdinalIgnoreCase));

            if (pluginHandle is not null)
            {
                _loadedPlugins.Remove(pluginHandle);
                hostEventDispatcher.UnregisterPlugin(pluginHandle);
            }
        }

        if (pluginHandle is null)
        {
            CH.Warning($"未找到已加载插件: {pluginName}");
            return Task.CompletedTask;
        }

        CH.Info($"已加入热卸载队列: {pluginHandle.Name}");
        if (TryQueuePluginBackgroundTask(() => ProcessPluginUnloadAsync(pluginHandle)) is null)
            CH.Warning($"程序正在退出，取消热卸载任务: {pluginHandle.Name}");

        return Task.CompletedTask;
    }

    private static async Task ProcessPluginUnloadAsync(
        LoadedPluginHandle pluginHandle)
    {
        PluginUnloadResult? unloadResult;
        await PluginUnloadSemaphore.WaitAsync();
        try
        {
            CH.Info($"开始热卸载插件: {pluginHandle.Name}");
            unloadResult = await pluginHandle.UnloadAsync();

            if (unloadResult.Error is not null)
            {
                CH.Error($"插件卸载失败: {unloadResult.Name} - {unloadResult.Error.Message}");
                return;
            }

            CH.Info($"插件逻辑已卸载，正在后台验证程序集释放: {unloadResult.Name}");
        }
        finally
        {
            PluginUnloadSemaphore.Release();
        }

        if (unloadResult.Error is null)
            if (TryQueuePluginBackgroundTask(async () =>
                {
                    // Let the unload call stack unwind before forcing collections, otherwise the async state machine
                    // that awaited OnUnload may temporarily keep the plugin instance alive.
                    await Task.Delay(300);

                    var assemblyUnloaded =
                        DllLoader<IBotPlugin>.WaitForUnload(unloadResult.AssemblyLoadContextWeakReference);
                    if (!assemblyUnloaded)
                    {
                        var aliveObjects = new List<string>();
                        if (unloadResult.PluginWeakReference?.IsAlive == true) aliveObjects.Add("plugin");

                        if (unloadResult.ContextWeakReference?.IsAlive == true) aliveObjects.Add("plugin-context");

                        if (aliveObjects.Count > 0)
                            CH.Warning($"热卸载诊断: {unloadResult.Name} 存活对象: {string.Join(", ", aliveObjects)}");

                        CH.Warning($"插件逻辑已卸载，但程序集仍有残留引用: {unloadResult.Name} ({unloadResult.AssemblyPath})");
                        return;
                    }

                    CH.Success($"插件热卸载成功: {unloadResult.Name}");
                }) is null)
                CH.Warning($"程序正在退出，跳过热卸载验证任务: {unloadResult.Name}");
    }

    private static Task ScheduleLoadPluginByName(
        string pluginRoot,
        HostEventDispatcher hostEventDispatcher,
        PluginRouteConfig routePolicy,
        string pluginNameOrPath)
    {
        lock (PluginLifecycleLock)
        {
            if (_isShuttingDown)
            {
                CH.Warning($"程序正在退出，忽略热加载请求: {pluginNameOrPath}");
                return Task.CompletedTask;
            }
        }

        var candidateAssemblies = ResolvePluginLoadCandidates(pluginRoot, pluginNameOrPath);
        if (candidateAssemblies.Count == 0)
        {
            CH.Warning($"未找到可加载插件: {pluginNameOrPath}");
            return Task.CompletedTask;
        }

        CH.Info($"已加入热加载队列: {pluginNameOrPath}");
        if (TryQueuePluginBackgroundTask(() => ProcessPluginLoadAsync(
                candidateAssemblies,
                hostEventDispatcher,
                routePolicy)) is null)
            CH.Warning($"程序正在退出，取消热加载任务: {pluginNameOrPath}");

        return Task.CompletedTask;
    }

    private static async Task ProcessPluginLoadAsync(
        IReadOnlyList<string> candidateAssemblies,
        HostEventDispatcher hostEventDispatcher,
        PluginRouteConfig routePolicy)
    {
        await PluginUnloadSemaphore.WaitAsync();
        try
        {
            await LoadPluginsAsync(
                candidateAssemblies,
                hostEventDispatcher,
                routePolicy);
        }
        finally
        {
            PluginUnloadSemaphore.Release();
        }
    }

    private static async Task LoadPluginsAsync(
        IReadOnlyList<string> pluginDlls,
        HostEventDispatcher hostEventDispatcher,
        PluginRouteConfig routePolicy)
    {
        foreach (var dll in pluginDlls) await LoadPluginAsync(dll, hostEventDispatcher, routePolicy);
    }

    private static async Task LoadPluginAsync(
        string dll,
        HostEventDispatcher hostEventDispatcher,
        PluginRouteConfig routePolicy)
    {
        DllLoader<IBotPlugin>? loader = null;
        try
        {
            var actualDllPath = Path.GetFullPath(dll);

            var isRootLevelPluginAssembly = string.Equals(
                Path.GetFullPath(Path.GetDirectoryName(dll) ?? _pluginRootPath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(_pluginRootPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

            IBotPlugin? tempPlugin = null;
            if (isRootLevelPluginAssembly)
            {
                //getplugininfo
                BotComponentMetadata? pluginInfo;
                string? pluginName;
                var tempLoader = new DllLoader<IBotPlugin>();
                
                try
                {
                    tempPlugin = tempLoader.Load(actualDllPath);
                    pluginInfo = tempPlugin.Metadata;
                    pluginName = tempPlugin.Name;

                    // getPluginInfo
                    if (pluginInfo.IsPluginSingleFile is true)
                    {
                        var pluginContext = new PluginContext(
                            Context,
                            tempPlugin.Name,
                            null,
                            groupId => routePolicy.AllowsGroup(tempPlugin.Name, groupId));

                        var metadata = tempPlugin.Metadata;
                        CH.Info($"开始加载插件: {metadata.Name} v{metadata.Version} ");

                        using (BotLog.BeginScope(pluginContext.Logger))
                        {
                            await tempPlugin.OnLoad(pluginContext);
                        }

                        var pluginHandle = new LoadedPluginHandle(
                            tempPlugin,
                            pluginContext,
                            tempLoader,
                            actualDllPath,
                            groupId => routePolicy.AllowsGroup(tempPlugin.Name, groupId));

                        lock (PluginLifecycleLock)
                        {
                            _loadedPlugins.Add(pluginHandle);
                            hostEventDispatcher.RegisterPlugin(pluginHandle);
                        }

                        CH.Success($"插件加载成功: {tempPlugin.Name} ({actualDllPath})");
                    }
                    else
                    {
                        // try
                        // {
                        //     await tempPlugin.OnUnload();
                        //     tempLoader.Unload();
                        //     tempPlugin = null;
                        // }
                        // catch (Exception e)
                        // {
                        //     BotLog.Error($"插件卸载失败: {pluginInfo.Name} - {e.Message}");
                        //     throw;
                        // }

                        //move plugin
                        try
                        {
                            var targetDllRootPath = Path.Combine(_pluginRootPath, pluginName);

                            Directory.CreateDirectory(targetDllRootPath);
                            File.Copy(actualDllPath, Path.Combine(targetDllRootPath, $"{pluginName}.dll"), true);
                            File.Delete(actualDllPath);
                        }
                        catch (Exception e)
                        {
                            BotLog.Error($"插件移动/删除出错: {pluginInfo.Name} - {e.Message}");
                            throw;
                        }
                        finally
                        {
                            tempLoader.Unload();
                            tempPlugin = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    BotLog.Error(e.Message);
                    return;
                }
            }

            if (tempPlugin is null)
            {
                loader = new DllLoader<IBotPlugin>();
                var plugin = loader.Load(actualDllPath);

                lock (PluginLifecycleLock)
                {
                    if (_loadedPlugins.Any(item =>
                            string.Equals(item.Name, plugin.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        CH.Warning($"插件名重复，已跳过：{plugin.Name} ({actualDllPath})");
                        loader.Unload();
                        return;
                    }
                }

                var pluginDirectory = Path.Combine(_pluginRootPath, plugin.Name);
                var pluginContext = new PluginContext(
                    Context,
                    plugin.Name,
                    pluginDirectory,
                    groupId => routePolicy.AllowsGroup(plugin.Name, groupId));

                var metadata = plugin.Metadata;
                CH.Info($"开始加载插件: {metadata.Name} v{metadata.Version} ");

                using (BotLog.BeginScope(pluginContext.Logger))
                {
                    await plugin.OnLoad(pluginContext);
                }

                var pluginHandle = new LoadedPluginHandle(
                    plugin,
                    pluginContext,
                    loader,
                    actualDllPath,
                    groupId => routePolicy.AllowsGroup(plugin.Name, groupId));

                lock (PluginLifecycleLock)
                {
                    _loadedPlugins.Add(pluginHandle);
                    hostEventDispatcher.RegisterPlugin(pluginHandle);
                }

                CH.Success($"插件加载成功: {plugin.Name} ({actualDllPath})");
                loader = null;
            }
        }
        catch (Exception ex)
        {
            CH.Error($"插件加载失败: {dll} - {ex.Message}");
            loader?.Unload();
        }
    }

    private static IReadOnlyList<string> ResolvePluginLoadCandidates(string pluginRoot, string pluginNameOrPath)
    {
        var normalizedInput = pluginNameOrPath.Trim();

        if (Path.HasExtension(normalizedInput) || normalizedInput.Contains(Path.DirectorySeparatorChar) ||
            normalizedInput.Contains(Path.AltDirectorySeparatorChar))
        {
            var fullPath = Path.IsPathRooted(normalizedInput)
                ? Path.GetFullPath(normalizedInput)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, normalizedInput));

            return File.Exists(fullPath) ? [fullPath] : [];
        }

        var directDll = Path.Combine(pluginRoot, $"{normalizedInput}.dll");
        if (File.Exists(directDll)) return [Path.GetFullPath(directDll)];

        var pluginDirectoryDll = Path.Combine(pluginRoot, normalizedInput, $"{normalizedInput}.dll");
        if (File.Exists(pluginDirectoryDll)) return [Path.GetFullPath(pluginDirectoryDll)];

        return EnumeratePluginEntryAssemblies(pluginRoot)
            .Where(path =>
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                var directoryName = new DirectoryInfo(Path.GetDirectoryName(path) ?? pluginRoot).Name;
                return string.Equals(fileName, normalizedInput, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(directoryName, normalizedInput, StringComparison.OrdinalIgnoreCase);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
