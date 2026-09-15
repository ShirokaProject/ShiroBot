using System.CommandLine;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Runtime.Loader;
using Avalonia;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Commands;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.Metadata;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Update;
using ShiroBot.Model.Discord;
using ShiroBot.Model.QQ;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Abstractions;
using CH = ShiroBot.Console.ConsoleOutput;

namespace ShiroBot;

public static class Program
{
    private static string BasePath => AppContext.BaseDirectory;

    /// <summary>
    /// 供 Avalonia 设计器（IDE Previewer）反射调用。Previewer 通过 <c>--method avalonia-remote</c>
    /// 加载本程序集并在入口类型上找此方法；它会自行替换 windowing platform，但要求 AppBuilder
    /// 自己声明渲染系统（这里固定 Skia，与运行时一致）。运行时本方法不参与 ShiroBot 的启动流程。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessHostApp>()
            .UseSkia()
            .UseHarfBuzz();

    public static async Task Main(string[] args)
    {
        var logHub = new HostLogHub();
        BotLog.SetDefault(new ConsoleLogger(logHub: logHub));

        var configOption = new Option<string?>("--config", "-c") { Description = "指定配置文件路径" };
        var adapterOption = new Option<string?>("--adapter") { Description = "指定适配器 DLL 路径" };
        var pluginOption = new Option<string?>("--plugin-dir") { Description = "指定插件文件夹路径（可以是相对路径或绝对路径）" };
        var noConsoleOption = new Option<bool>("--no-console") { Description = "禁用控制台交互输入" };

        var rootCommand = new RootCommand("ShiroBot 主程序") { adapterOption, pluginOption, configOption, noConsoleOption };
        var parserResult = rootCommand.Parse(args);

        CH.Info("ShiroBot 启动中...");
        CH.Info(BotMetadataProvider.StartupVersionText);

        var sharedAssemblies = new SharedAssemblyResolver();
        sharedAssemblies.Register(["ShiroBot.SDK"], AssemblyLoadContext.Default);
        var modelPackages = new ModelPackageRegistry(sharedAssemblies);
        BotContext? botContext;
        PluginManager? pluginManager = null;
        CoreConfigWatcher? configWatcher = null;
        HostHttpServer? hostHttpServer = null;
        AdapterManager? adapterManager = null;
        ComponentFileWatcher? componentWatcher = null;
        var runtimeState = new HostRuntimeState(DateTimeOffset.UtcNow);
        var shutdownRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PosixSignalRegistration? sigintRegistration = null;
        PosixSignalRegistration? sigtermRegistration = null;

        void RequestShutdown() => shutdownRequested.TrySetResult();

        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            RequestShutdown();
        };
        global::System.Console.CancelKeyPress += cancelKeyPressHandler;

        if (!OperatingSystem.IsWindows())
        {
            sigintRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
            {
                context.Cancel = true;
                RequestShutdown();
            });
            sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                RequestShutdown();
            });
        }

        try
        {
            // ─── 核心配置 ───
            var coreConfigPath = Path.Combine(BasePath, "config.toml");
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

            var coreConfigManager = new ConfigManager(coreConfigPath);
            var coreConfig = await coreConfigManager.LoadCoreConfig();
            EnsureApiAuthKey(coreConfig, coreConfigManager, coreConfigPath);
            CH.IsEnabled = coreConfig.EnableLog;
            var groupRoutePolicy = coreConfig.PluginRoutes;

            // ─── 插件与平台 Model 目录 ───
            var pluginRootPath = Path.Combine(BasePath, "plugins");
            var commandPluginDirectory = parserResult.GetValue(pluginOption);
            if (!string.IsNullOrWhiteSpace(commandPluginDirectory))
            {
                BotLog.Info("检测到指定插件目录: " + commandPluginDirectory);
                if (!Directory.Exists(commandPluginDirectory))
                    BotLog.Info("指定插件目录不存在，回退到默认目录: " + pluginRootPath);
                else
                    pluginRootPath = commandPluginDirectory;
            }

            if (!Directory.Exists(pluginRootPath))
            {
                BotLog.Info("插件目录不存在，创建目录: " + pluginRootPath);
                Directory.CreateDirectory(pluginRootPath);
            }

            // ─── 平台 Model 加载 ───
            modelPackages.RegisterBuiltIn(typeof(DiscordUser).Assembly);
            modelPackages.RegisterBuiltIn(typeof(QGroup).Assembly);
            modelPackages.RegisterBuiltIn(typeof(TelegramUser).Assembly);
            runtimeState.SetModelsCount(modelPackages.GetPackages().Count);

            // ─── 适配器加载 ───
            var adapterRoot = Path.Combine(BasePath, "adapters");
            if (!Directory.Exists(adapterRoot))
            {
                BotLog.Info("适配器目录不存在，创建目录: " + adapterRoot);
                Directory.CreateDirectory(adapterRoot);
            }

            var adapterPaths = ResolveAdapterPaths(coreConfig, parserResult.GetValue(adapterOption));

            // ─── BotContext + 基础设施 ───
            var webPublicBaseUrl = string.IsNullOrWhiteSpace(coreConfig.Api.PublicBaseUrl)
                ? (coreConfig.Api.ListenUrls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)) ?? coreConfig.Api.ListenUrl)
                : coreConfig.Api.PublicBaseUrl;
            var webHostContext = new WebHostContext(webPublicBaseUrl, coreConfig.Api.Enable);
            botContext = new BotContext(null, coreConfig.OwnerList, coreConfig.AdminList, webHostContext);
            Updater.Initialize(
                () => botContext.OwnerList,
                (ownerId, content) => botContext.Message.SendDirectMessageAsync(ownerId, content),
                coreConfig.GithubProxy);

            var hostEventDispatcher = new HostEventDispatcher(new Lock(), botContext.ReplySubscriptions, runtimeState, logHub);
            pluginManager = new PluginManager(botContext, sharedAssemblies, modelPackages, runtimeState, logHub);

            // ─── Avalonia 渲染集成 ───
            try
            {
                var avaloniaRenderer = AvaloniaIntegration.Initialize(coreConfig.AvaloniaTheme);
                botContext.AttachRenderer(avaloniaRenderer);
                sharedAssemblies.Register(
                    ["Avalonia", "SkiaSharp", "HarfBuzzSharp", "MicroCom"],
                    AssemblyLoadContext.Default);
                CH.Success("Avalonia 渲染服务已启用。");
            }
            catch (Exception ex)
            {
                CH.Error("Avalonia 渲染服务启动失败: " + ex.Message);
            }

            // ─── 配置热重载 ───
            configWatcher = new CoreConfigWatcher(coreConfigPath, coreConfig, botContext);

            // ─── 插件加载 ───
            pluginManager.PluginRootPath = pluginRootPath;
            pluginManager.EnableFileHotReload(hostEventDispatcher, groupRoutePolicy);

            // ─── 适配器事件桥接 ───
            var commandHandler = new HostCommandHandler(
                botContext,
                pluginManager,
                hostEventDispatcher,
                groupRoutePolicy,
                coreConfig,
                coreConfigManager,
                coreConfigPath);
            var adapterBridge = new AdapterEventBridge(hostEventDispatcher);
            adapterManager = new AdapterManager(
                adapterRoot,
                sharedAssemblies,
                modelPackages,
                botContext,
                adapterBridge,
                runtimeState,
                logHub,
                commandHandler.HandleDirectMessageAsync);
            if (adapterPaths.Count > 0)
            {
                await adapterManager.LoadAsync(adapterPaths).ConfigureAwait(false);
                CH.Success($"已加载 {adapterPaths.Count} 个 Adapter。 ");
            }
            else
            {
                runtimeState.SetAdapter("none", "not_loaded");
                runtimeState.RecordEvent("以无 adapter 模式启动");
            }

            var reloadCoordinator = new ComponentReloadCoordinator(
                adapterManager,
                pluginManager,
                hostEventDispatcher,
                groupRoutePolicy);
            componentWatcher = new ComponentFileWatcher(adapterPaths, reloadCoordinator);
            hostHttpServer = await HostHttpServer.StartAsync(
                coreConfig.Api,
                coreConfigManager,
                coreConfigPath,
                pluginManager,
                hostEventDispatcher,
                groupRoutePolicy,
                webHostContext,
                runtimeState,
                logHub,
                botContext,
                modelPackages,
                adapterManager,
                reloadCoordinator);
            if (coreConfig.Api.Enable)
            {
                CH.Success("API 地址: " + webPublicBaseUrl);
                if (coreConfig.Api.Auth.Enable)
                {
                    CH.Warning("API 鉴权密钥: " + coreConfig.Api.Auth.Key);
                }
            }

            // ─── 控制台交互 ───
            var configuredConsoleOption = parserResult.GetValue(noConsoleOption);
            var hasConsole = Environment.UserInteractive && !global::System.Console.IsInputRedirected && !global::System.Console.IsOutputRedirected;
            var enableConsoleInput = hasConsole && !configuredConsoleOption && !coreConfig.DisableConsoleInput;

            if (enableConsoleInput)
            {
                var exitRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var consoleReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = Task.Run(() => commandHandler.RunConsoleLoop(exitRequested, consoleReady));
                await consoleReady.Task;

                CH.Info("控制台已就绪，开始在后台加载插件...");
                _ = pluginManager.ScheduleInitialPluginLoad(
                    PluginManager.EnumeratePluginEntryAssemblies(pluginRootPath).ToList(),
                    hostEventDispatcher,
                    groupRoutePolicy);

                var completedTask = await Task.WhenAny(exitRequested.Task, shutdownRequested.Task);
                if (completedTask == shutdownRequested.Task)
                {
                    CH.Info("收到进程停止信号，正在安全退出...");
                }
                return;
            }

            var reasons = new List<string>();
            if (!hasConsole) reasons.Add("检测到非交互终端");
            if (configuredConsoleOption) reasons.Add("命令行参数 --no-console 已启用");
            if (coreConfig.DisableConsoleInput) reasons.Add("配置项 disable_console_input = true");
            CH.Info($"已禁用控制台命令输入: {string.Join("，", reasons)}");

            CH.Info("开始在后台加载插件...");
            _ = pluginManager.ScheduleInitialPluginLoad(
                PluginManager.EnumeratePluginEntryAssemblies(pluginRootPath).ToList(),
                hostEventDispatcher,
                groupRoutePolicy);

            await shutdownRequested.Task;
            CH.Info("收到进程停止信号，正在安全退出...");
        }
        catch (Exception ex)
        {
            CH.Error("程序启动失败: " + ex.Message);
            CH.Warning("按任意键退出...");
            if (CanReadInteractiveKey()) global::System.Console.ReadKey();
        }
        finally
        {
            global::System.Console.CancelKeyPress -= cancelKeyPressHandler;
            sigintRegistration?.Dispose();
            sigtermRegistration?.Dispose();

            pluginManager?.BeginShutdown();
            componentWatcher?.Dispose();
            configWatcher?.Dispose();

            if (hostHttpServer is not null)
            {
                try
                {
                    await hostHttpServer.DisposeAsync();
                }
                catch (Exception ex)
                {
                    CH.Warning("停止宿主 API 服务时出现异常: " + ex.Message);
                }
            }

            if (adapterManager is not null)
            {
                try
                {
                    await adapterManager.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CH.Warning("停止适配器时出现异常: " + ex.Message);
                }
            }

            try
            {
                AvaloniaIntegration.Shutdown();
            }
            catch (Exception ex)
            {
                CH.Warning("关停 Avalonia 渲染服务时出现异常: " + ex.Message);
            }

            // Hot unload performs plugin cleanup and collectible ALC checks. Process teardown
            // releases the remaining plugin and adapter contexts directly.
        }
    }

    private static bool CanReadInteractiveKey() =>
        Environment.UserInteractive && !global::System.Console.IsInputRedirected && !global::System.Console.IsOutputRedirected;

    private static void EnsureApiAuthKey(CoreConfig coreConfig, ConfigManager manager, string configPath)
    {
        if (!coreConfig.Api.Auth.Enable || !string.IsNullOrWhiteSpace(coreConfig.Api.Auth.Key)) return;

        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        coreConfig.Api.Auth.Key = Convert.ToHexString(bytes).ToLowerInvariant();
        manager.SaveConfig(configPath, coreConfig);
    }

    private static IReadOnlyList<string> ResolveAdapterPaths(CoreConfig coreConfig, string? commandAdapterPath)
    {
        if (!string.IsNullOrWhiteSpace(commandAdapterPath))
        {
            BotLog.Info("检测到命令行适配器路径，使用指定的适配器文件: " + commandAdapterPath);
            return File.Exists(commandAdapterPath) ? [Path.GetFullPath(commandAdapterPath)] : [];
        }

        var configured = coreConfig.Protocols.Length > 0
            ? coreConfig.Protocols
            : string.IsNullOrWhiteSpace(coreConfig.Protocol) ? [] : [coreConfig.Protocol];
        var paths = new List<string>();
        foreach (var value in configured.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var path = ResolveAdapterPath(value);
            if (path is null)
                throw new FileNotFoundException($"未找到配置的 Adapter: {value}");
            paths.Add(path);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? ResolveAdapterPath(string configured)
    {
        if (File.Exists(configured)) return Path.GetFullPath(configured);
        var name = configured.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? configured[..^4]
            : configured;
        var root = Path.Combine(BasePath, "adapters");
        var flat = Path.Combine(root, name + ".dll");
        if (File.Exists(flat)) return flat;
        var folder = Path.Combine(root, name, name + ".dll");
        return File.Exists(folder) ? folder : null;
    }

}
