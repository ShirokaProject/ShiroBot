using System.CommandLine;
using System.Security.Cryptography;
using System.Runtime.Loader;
using Avalonia;
using ShiroBot.Core;
using ShiroBot.Hosting;
using ShiroBot.Hosting.Context;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using CH = ShiroBot.Core.ConsoleHelper;

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
        AppBuilder.Configure<AvaloniaIntegration.HeadlessHostApp>()
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
        CH.Info(BotMetaDataProvider.StartupVersionText);

        var sharedAssemblies = new SharedAssemblyResolver();
        _ = typeof(IncomingMessage).Assembly;
        sharedAssemblies.Register(["ShiroBot.SDK", "ShiroBot.Model"], AssemblyLoadContext.Default);
        BotContext? botContext;
        PluginManager? pluginManager = null;
        CoreConfigWatcher? configWatcher = null;
        HostHttpServer? hostHttpServer = null;
        IBotAdapter? adapter = null;
        DllLoader<IBotAdapter>? adapterLoader = null;
        var runtimeState = new HostRuntimeState(DateTimeOffset.UtcNow);

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

            // ─── 适配器加载 ───
            var adapterRoot = Path.Combine(BasePath, "adapters");
            if (!Directory.Exists(adapterRoot))
            {
                BotLog.Info("适配器目录不存在，创建目录: " + adapterRoot);
                Directory.CreateDirectory(adapterRoot);
            }

            var adapterPath = ResolveAdapterPath(coreConfig, parserResult.GetValue(adapterOption));
            if (adapterPath is null)
            {
                CH.Warning("请确认 adapters 目录下存在对应的适配器文件，或在 config.toml 中配置 protocol...");
                if (CanReadInteractiveKey()) Console.ReadKey();
                return;
            }

            CH.Log("开始加载适配器: " + adapterPath);
            var adapterDependencies = await PluginRuntimeDependencyManager.PrepareAsync(
                adapterPath,
                Path.GetDirectoryName(adapterPath) ?? adapterRoot).ConfigureAwait(false);
            adapterLoader = new DllLoader<IBotAdapter>(
                collectible: true,
                shared: sharedAssemblies,
                dependencies: adapterDependencies);
            adapter = adapterLoader.Load(adapterPath);
            adapter.Config = ConfigContext.ForAdapter(ResolveAdapterConfigPath(adapterRoot, adapterPath));
            adapter.Logger = new ConsoleLogger($"[Adapter:{adapter.Name}]", logHub);

            var adapterMetadata = BotAdapterMetadata.Resolve(adapter);
            CH.Log($"适配器信息: {adapterMetadata.Name} v{adapterMetadata.Version}");

            using (BotLog.BeginScope(adapter.Logger))
            {
                await adapter.StartAsync();
            }
            var adapterDisplayName = string.IsNullOrWhiteSpace(adapterMetadata.Name) ? adapter.Name : adapterMetadata.Name;
            runtimeState.SetAdapter(adapterDisplayName, "connected");
            logHub.RegisterSource(
                adapter.Name,
                adapterMetadata.Description ?? $"{adapterDisplayName} 适配器日志",
                adapterDisplayName);
            runtimeState.RecordEvent($"{adapterDisplayName} 连接成功");

            CH.Success("加载适配器成功: " + adapter.Name);

            // ─── BotContext + 基础设施 ───
            var webPublicBaseUrl = string.IsNullOrWhiteSpace(coreConfig.Api.PublicBaseUrl)
                ? (coreConfig.Api.ListenUrls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)) ?? coreConfig.Api.ListenUrl)
                : coreConfig.Api.PublicBaseUrl;
            var webHostContext = new WebHostContext(webPublicBaseUrl, coreConfig.Api.Enable);
            botContext = new BotContext(adapter, coreConfig.OwnerList, coreConfig.AdminList, webHostContext);
            Updater.Initialize(
                () => botContext.OwnerList,
                (ownerId, content) => botContext.Message.SendPrivateMessageAsync(ownerId, content),
                coreConfig.GithubProxy);

            var hostEventDispatcher = new HostEventDispatcher(new Lock(), botContext.ReplySubscriptions, runtimeState, logHub);
            pluginManager = new PluginManager(botContext, sharedAssemblies, runtimeState, logHub);

            hostHttpServer = await HostHttpServer.StartAsync(
                coreConfig.Api,
                coreConfig,
                coreConfigManager,
                coreConfigPath,
                pluginManager,
                hostEventDispatcher,
                groupRoutePolicy,
                webHostContext,
                runtimeState,
                logHub);
            if (coreConfig.Api.Enable)
            {
                CH.Success("API 地址: " + webPublicBaseUrl);
                if (coreConfig.Api.Auth.Enable)
                {
                    CH.Warning("API 鉴权密钥: " + coreConfig.Api.Auth.Key);
                }
            }

            // ─── Avalonia 渲染集成 ───
            try
            {
                var avaloniaRenderer = AvaloniaIntegration.AvaloniaIntegration.Initialize(coreConfig.AvaloniaTheme);
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

            pluginManager.PluginRootPath = pluginRootPath;

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
            adapterBridge.Bridge(
                adapter.Event,
                pluginRootPath,
                groupRoutePolicy,
                commandHandler.HandleFriendMessageAsync);

            // ─── 控制台交互 ───
            var configuredConsoleOption = parserResult.GetValue(noConsoleOption);
            var hasConsole = Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;
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

                await exitRequested.Task;
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
            configWatcher?.Dispose();

            if (hostHttpServer is not null)
            {
                await hostHttpServer.DisposeAsync();
            }

            if (adapter is not null)
            {
                try
                {
                    await adapter.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CH.Warning("停止适配器时出现异常: " + ex.Message);
                }
            }

            if (pluginManager is not null)
            {
                await pluginManager.AwaitPluginBackgroundTasksAsync();

                foreach (var pluginHandle in Enumerable.Reverse(pluginManager.GetLoadedPluginSnapshot()))
                {
                    var result = await pluginHandle.UnloadAsync();
                    if (result.Error is not null) CH.Error($"插件卸载失败: {result.Name} - {result.Error.Message}");

                    var assemblyUnloaded = DllLoader<IBotPlugin>.WaitForUnload(result.AssemblyLoadContextWeakReference);
                    if (!assemblyUnloaded) CH.Warning($"插件程序集未完全卸载，可能仍有引用残留: {result.Name} ({result.AssemblyPath})");
                }
            }

            try
            {
                AvaloniaIntegration.AvaloniaIntegration.Shutdown();
            }
            catch (Exception ex)
            {
                CH.Warning("关停 Avalonia 渲染服务时出现异常: " + ex.Message);
            }

            adapter = null;
            var adapterAlc = adapterLoader?.BeginUnload();
            if (!DllLoader<IBotAdapter>.WaitForUnload(adapterAlc))
            {
                CH.Warning("适配器程序集未完全卸载，可能仍有后台引用残留。");
            }
        }
    }

    private static bool CanReadInteractiveKey() =>
        Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private static void EnsureApiAuthKey(CoreConfig coreConfig, ConfigManager manager, string configPath)
    {
        if (!coreConfig.Api.Auth.Enable || !string.IsNullOrWhiteSpace(coreConfig.Api.Auth.Key)) return;

        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        coreConfig.Api.Auth.Key = Convert.ToHexString(bytes).ToLowerInvariant();
        manager.SaveConfig(configPath, coreConfig);
    }

    private static string? ResolveAdapterPath(CoreConfig coreConfig, string? commandAdapterPath)
    {
        var adapterName = coreConfig.Protocol.EndsWith("dll", StringComparison.OrdinalIgnoreCase)
            ? coreConfig.Protocol[..^4]
            : coreConfig.Protocol;
        var adapterPath = Path.Combine(BasePath, "adapters", $"{adapterName}.dll");

        if (!File.Exists(adapterPath) &&
            File.Exists(Path.Combine(BasePath, "adapters", adapterName, $"{adapterName}.dll")))
            adapterPath = Path.Combine(BasePath, "adapters", adapterName, $"{adapterName}.dll");

        if (!string.IsNullOrWhiteSpace(commandAdapterPath))
        {
            BotLog.Info("检测到命令行适配器路径，使用指定的适配器文件: " + commandAdapterPath);
            adapterPath = commandAdapterPath;
        }

        if (!File.Exists(adapterPath))
        {
            var fallbackDll = Directory.EnumerateFiles(Path.Combine(BasePath, "adapters"),
                "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (fallbackDll is not null)
                adapterPath = fallbackDll;
            else
                fallbackDll = Directory
                    .EnumerateDirectories(Path.Combine(BasePath, "adapters"))
                    .SelectMany(folder => Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
                    .FirstOrDefault();
            BotLog.Warning(fallbackDll is not null
                ? $"未配置适配器，自动选择适配器: {fallbackDll}"
                : "未找到任何适配器文件，请确认 adapters 目录下存在适配器 DLL 文件。");
        }

        return File.Exists(adapterPath) ? adapterPath : null;
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

}
