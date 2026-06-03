using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed class PluginManager(BotContext botContext, SharedAssemblyResolver sharedAssemblies)
{
    private readonly List<LoadedPluginHandle> _loadedPlugins = [];
    private readonly List<Task> _pluginBackgroundTasks = [];
    private bool _isShuttingDown;

    public string PluginRootPath { get; set; } = string.Empty;

    private Lock PluginLifecycleLock { get; } = new();

    private SemaphoreSlim PluginUnloadSemaphore { get; } = new(1, 1);

    private SharedAssemblyResolver SharedAssemblies { get; } = sharedAssemblies;

    public static IEnumerable<string> EnumeratePluginEntryAssemblies(string pluginRoot)
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

    public List<string> GetLoadedPluginNames()
    {
        lock (PluginLifecycleLock)
        {
            return _loadedPlugins
                .Select(plugin => plugin.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public List<LoadedPluginHandle> GetLoadedPluginSnapshot()
    {
        lock (PluginLifecycleLock)
        {
            return _loadedPlugins.ToList();
        }
    }

    private Task[] BeginShutdownAndGetBackgroundTasks()
    {
        lock (PluginLifecycleLock)
        {
            _isShuttingDown = true;
            return _pluginBackgroundTasks.ToArray();
        }
    }

    public async Task AwaitPluginBackgroundTasksAsync()
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

    private Task? TryQueuePluginBackgroundTask(Func<Task> taskFactory)
    {
        Task? task;
        lock (PluginLifecycleLock)
        {
            if (_isShuttingDown) return null;

            task = Task.Run(taskFactory);
            _pluginBackgroundTasks.Add(task);
        }

        _ = task.ContinueWith(
            _ =>
            {
                lock (PluginLifecycleLock)
                {
                    _pluginBackgroundTasks.Remove(task);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    public Task ScheduleUnloadPluginByName(
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

    private async Task ProcessPluginUnloadAsync(
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

    public Task ScheduleLoadPluginByName(
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

        var candidateAssemblies = ResolvePluginLoadCandidates(PluginRootPath, pluginNameOrPath);
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

    private async Task ProcessPluginLoadAsync(
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

    public async Task LoadPluginsAsync(
        IReadOnlyList<string> pluginDlls,
        HostEventDispatcher hostEventDispatcher,
        PluginRouteConfig routePolicy,
        bool isInitialBoot = false)
    {
        _ = isInitialBoot; // 保留参数以兼容外部调用约定，已不再使用两阶段加载。
        foreach (var dll in pluginDlls)
        {
            await LoadPluginAsync(dll, hostEventDispatcher, routePolicy);
        }
    }

    private async Task LoadPluginAsync(
        string dll,
        HostEventDispatcher hostEventDispatcher,
        PluginRouteConfig routePolicy)
    {
        DllLoader<IBotPlugin>? loader = null;
        try
        {
            var actualDllPath = Path.GetFullPath(dll);

            var isRootLevelPluginAssembly = string.Equals(
                Path.GetFullPath(Path.GetDirectoryName(dll) ?? PluginRootPath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(PluginRootPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

            IBotPlugin? tempPlugin = null;
            if (isRootLevelPluginAssembly)
            {
                //getplugininfo
                string? pluginName;
                var tempLoader = CreateLoader();

                try
                {
                    tempPlugin = tempLoader.Load(actualDllPath);
                    var pluginInfo = tempPlugin.Metadata;
                    pluginName = tempPlugin.Name;

                    // getPluginInfo
                    if (pluginInfo.IsPluginSingleFile is true)
                    {
                        var pluginContext = CreatePluginContext(
                            tempPlugin.Name,
                            pluginDirectory: null,
                            routePolicy);

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
                            CreateGroupRouteFilter(tempPlugin.Name, routePolicy));

                        lock (PluginLifecycleLock)
                        {
                            _loadedPlugins.Add(pluginHandle);
                            hostEventDispatcher.RegisterPlugin(pluginHandle);
                        }

                        CH.Success($"插件加载成功: {tempPlugin.Name} ({actualDllPath})");
                    }
                    else
                    {
                        //move plugin
                        try
                        {
                            var targetDllRootPath = Path.Combine(PluginRootPath, pluginName);

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
                loader = CreateLoader();
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

                // 配置目录使用 dll 实际所在目录；若 dll 直接放在 plugins 根目录（单文件插件），则不绑定配置目录。
                var dllDirectory = Path.GetFullPath(Path.GetDirectoryName(actualDllPath) ?? PluginRootPath)
                    .TrimEnd(Path.DirectorySeparatorChar);
                var normalizedPluginRoot = Path.GetFullPath(PluginRootPath).TrimEnd(Path.DirectorySeparatorChar);
                var pluginDirectory = string.Equals(dllDirectory, normalizedPluginRoot, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : dllDirectory;
                var pluginContext = CreatePluginContext(
                    plugin.Name,
                    pluginDirectory,
                    routePolicy);

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
                    CreateGroupRouteFilter(plugin.Name, routePolicy));

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

    public DllLoader<IBotPlugin> CreateLoader() =>
        new(collectible: true, shared: SharedAssemblies);

    /// <summary>
    /// 仅捕获 plugin 名（string）和 routePolicy 引用，不要让闭包捕获 <see cref="IBotPlugin"/>
    /// 实例本身——否则 LoadedPluginHandle 永远握着 plugin 引用，可回收 ALC 卸不掉。
    /// </summary>
    public static Func<long, bool> CreateGroupRouteFilter(string pluginName, PluginRouteConfig routePolicy) =>
        groupId => routePolicy.AllowsGroup(pluginName, groupId);

    public PluginContext CreatePluginContext(
        string pluginName,
        string? pluginDirectory,
        PluginRouteConfig routePolicy) =>
        new(
            botContext,
            pluginName,
            pluginDirectory,
            groupId => routePolicy.AllowsGroup(pluginName, groupId));

    public IReadOnlyList<string> ResolvePluginLoadCandidates(string pluginRoot, string pluginNameOrPath)
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
