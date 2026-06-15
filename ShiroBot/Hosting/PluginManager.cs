using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed record PluginProbeInfo(
    string TypeFullName,
    string Id,
    string Name,
    string Version,
    string? Description,
    string? GithubRepo,
    bool IsPluginSingleFile);

internal sealed record PluginProbeCacheEntry(long Length, DateTime LastWriteTimeUtc, PluginProbeInfo? Info);

internal sealed class PluginManager(BotContext botContext, SharedAssemblyResolver sharedAssemblies)
{
    private readonly List<LoadedPluginHandle> _loadedPlugins = [];
    private readonly List<Task> _pluginBackgroundTasks = [];
    private readonly ConcurrentDictionary<string, PluginProbeCacheEntry> _probeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _dirtyProbePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _suppressedWatcherPaths = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _pluginRootWatcher;
    private string? _watchedPluginRoot;
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

    public List<string> GetLoadablePluginCandidates(bool includePluginNames = true)
    {
        if (string.IsNullOrWhiteSpace(PluginRootPath) || !Directory.Exists(PluginRootPath))
            return [];

        var pluginRoot = Path.GetFullPath(PluginRootPath).TrimEnd(Path.DirectorySeparatorChar);
        EnsurePluginRootWatcher(pluginRoot);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyPath in EnumerateLoadableCandidateAssemblies(pluginRoot))
        {
            var pluginInfo = TryProbePluginInfoCached(assemblyPath);
            if (pluginInfo is null) continue;
            if (!seenPluginIds.Add(pluginInfo.Id)) continue;

            var parent = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrWhiteSpace(parent)) continue;

            var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            var candidate = string.Equals(normalizedParent, pluginRoot, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(assemblyPath)
                : new DirectoryInfo(normalizedParent).Name;
            var relativeCandidate = Path.GetRelativePath(pluginRoot, assemblyPath)
                .Replace(Path.DirectorySeparatorChar, '/');

            if (!string.IsNullOrWhiteSpace(relativeCandidate) && !relativeCandidate.StartsWith("..", StringComparison.Ordinal))
            {
                candidates.Add(relativeCandidate);
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                candidates.Add(candidate);
            }

            if (includePluginNames)
            {
                if (!string.IsNullOrWhiteSpace(pluginInfo.Id))
                {
                    candidates.Add(pluginInfo.Id);
                }
            }
        }

        return candidates
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void EnsurePluginRootWatcher(string pluginRoot)
    {
        if (string.Equals(_watchedPluginRoot, pluginRoot, StringComparison.OrdinalIgnoreCase)) return;

        _pluginRootWatcher?.Dispose();
        _watchedPluginRoot = pluginRoot;
        _pluginRootWatcher = new FileSystemWatcher(pluginRoot)
        {
            Filter = "*",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        _pluginRootWatcher.Changed += (_, e) => MarkProbeDirty(e.FullPath);
        _pluginRootWatcher.Created += (_, e) => HandleCreatedPluginPath(e.FullPath);
        _pluginRootWatcher.Deleted += (_, e) => RemoveProbeCache(e.FullPath);
        _pluginRootWatcher.Renamed += (_, e) =>
        {
            RemoveProbeCache(e.OldFullPath);
            HandleCreatedPluginPath(e.FullPath);
        };
    }

    private void MarkProbeDirty(string path)
    {
        if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            _dirtyProbePaths[Path.GetFullPath(path)] = 1;
        }
    }

    private void HandleCreatedPluginPath(string path)
    {
        if (IsWatcherPathSuppressed(path)) return;

        if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            HandleCreatedPluginFile(path);
            return;
        }

        _ = Task.Run(async () =>
        {
            var fullPath = Path.GetFullPath(path);
            for (var i = 0; i < 20; i++)
            {
                if (Directory.Exists(fullPath)) break;
                await Task.Delay(200).ConfigureAwait(false);
            }

            if (!Directory.Exists(fullPath)) return;

            var directoryName = new DirectoryInfo(fullPath).Name;
            var entryDll = Path.Combine(fullPath, $"{directoryName}.dll");
            if (File.Exists(entryDll))
            {
                HandleCreatedPluginFile(entryDll);
            }
        });
    }

    private void HandleCreatedPluginFile(string path)
    {
        MarkProbeDirty(path);
        if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)) return;

        _ = Task.Run(async () =>
        {
            var fullPath = Path.GetFullPath(path);
            await WaitForFileReadyAsync(fullPath).ConfigureAwait(false);
            if (IsWatcherPathSuppressed(fullPath)) return;

            var info = TryProbePluginInfoCached(fullPath);
            if (info is null)
            {
                CH.Info($"检测到新 DLL，非插件已忽略: {Path.GetFileName(fullPath)}");
                return;
            }

            CH.Info($"检测到新插件: {info.Name} ({info.Id}) - {fullPath}");
        });
    }

    private static async Task WaitForFileReadyAsync(string path)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                if (!File.Exists(path)) return;
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 0) return;
            }
            catch (IOException)
            {
                // File is still being copied.
            }
            catch (UnauthorizedAccessException)
            {
                // File is still being copied or locked by another process.
            }

            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private void SuppressWatcherPath(string path, int seconds = 10)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _suppressedWatcherPaths[NormalizeWatcherPath(path)] = DateTime.UtcNow.AddSeconds(seconds);
    }

    private bool IsWatcherPathSuppressed(string path)
    {
        var now = DateTime.UtcNow;
        var fullPath = NormalizeWatcherPath(path);

        foreach (var (suppressedPath, expiresAt) in _suppressedWatcherPaths.ToArray())
        {
            if (expiresAt <= now)
            {
                _suppressedWatcherPaths.TryRemove(suppressedPath, out _);
                continue;
            }

            if (string.Equals(fullPath, suppressedPath, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(suppressedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeWatcherPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void RemoveProbeCache(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Path.GetExtension(fullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var directoryPrefix = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var cachedPath in _probeCache.Keys.Where(item => item.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _probeCache.TryRemove(cachedPath, out _);
                _dirtyProbePaths.TryRemove(cachedPath, out _);
            }

            return;
        }

        _probeCache.TryRemove(fullPath, out _);
        _dirtyProbePaths.TryRemove(fullPath, out _);
    }

    public List<LoadedPluginHandle> GetLoadedPluginSnapshot()
    {
        lock (PluginLifecycleLock)
        {
            return _loadedPlugins.ToList();
        }
    }

    private static IEnumerable<string> EnumerateLoadableCandidateAssemblies(string pluginRoot)
    {
        var sharedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShiroBot.SDK.dll",
            "ShiroBot.Model.dll",
            "ShiroBot.AvaloniaSdk.dll",
            "ShiroBot.AvaloniaIntegration.dll"
        };
        var yieldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in EnumeratePluginEntryAssemblies(pluginRoot).Where(yieldedPaths.Add))
        {
            yield return entry;
        }

        foreach (var directory in Directory.EnumerateDirectories(pluginRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                         .Where(dll => !sharedAssemblies.Contains(Path.GetFileName(dll)))
                         .OrderBy(dll => dll, StringComparer.OrdinalIgnoreCase)
                         .Select(Path.GetFullPath)
                         .Where(yieldedPaths.Add))
            {
                yield return dll;
            }
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
        var task = TryQueuePluginBackgroundTask(() => ProcessPluginUnloadAsync(pluginHandle));
        if (task is null)
        {
            CH.Warning($"程序正在退出，取消热卸载任务: {pluginHandle.Name}");
            return Task.CompletedTask;
        }

        return task;
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

        if (unloadResult.Error is not null)
        {
            return;
        }

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
        var task = TryQueuePluginBackgroundTask(() => ProcessPluginLoadAsync(
                candidateAssemblies,
                hostEventDispatcher,
                routePolicy));
        if (task is null)
        {
            CH.Warning($"程序正在退出，取消热加载任务: {pluginNameOrPath}");
            return Task.CompletedTask;
        }

        return task;
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
            var pluginInfo = ProbePluginInfo(actualDllPath);

            lock (PluginLifecycleLock)
            {
                if (IsPluginLoaded(pluginInfo.Id))
                {
                    CH.Warning($"插件重复，已跳过：{pluginInfo.Id} ({actualDllPath})");
                    return;
                }
            }

            var isRootLevelPluginAssembly = string.Equals(
                Path.GetFullPath(Path.GetDirectoryName(dll) ?? PluginRootPath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(PluginRootPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

            if (isRootLevelPluginAssembly)
            {
                try
                {
                    if (pluginInfo.IsPluginSingleFile is true)
                    {
                        loader = CreateLoader();
                        var plugin = loader.Load(actualDllPath, pluginInfo.TypeFullName);

                        var pluginContext = CreatePluginContext(
                            pluginInfo.Id,
                            pluginDirectory: null,
                            routePolicy);

                        CH.Info($"开始加载插件: {pluginInfo.Name} v{pluginInfo.Version} ");

                        using (BotLog.BeginScope(pluginContext.Logger))
                        {
                            await plugin.OnLoad(pluginContext);
                        }

                        var pluginHandle = new LoadedPluginHandle(
                            plugin,
                            pluginContext,
                            loader,
                            actualDllPath,
                            pluginInfo,
                            CreateGroupRouteFilter(pluginInfo.Id, routePolicy));

                        lock (PluginLifecycleLock)
                        {
                            if (IsPluginLoaded(pluginInfo.Id))
                            {
                                CH.Warning($"插件重复，已跳过：{pluginInfo.Id} ({actualDllPath})");
                                loader.Unload();
                                return;
                            }

                            _loadedPlugins.Add(pluginHandle);
                            hostEventDispatcher.RegisterPlugin(pluginHandle);
                        }

                        CH.Success($"插件加载成功: {pluginInfo.Id} ({actualDllPath})");
                        loader = null;
                        return;
                    }
                    else
                    {
                        //move plugin
                        var pluginDisplayName = pluginInfo.Name;
                        var targetDllRootPath = Path.Combine(PluginRootPath, pluginInfo.Id);
                        var targetDllPath = Path.Combine(targetDllRootPath, $"{pluginInfo.Id}.dll");

                        try
                        {
                            SuppressWatcherPath(targetDllRootPath);
                            SuppressWatcherPath(targetDllPath);
                            Directory.CreateDirectory(targetDllRootPath);
                            File.Copy(actualDllPath, targetDllPath, true);
                            File.Delete(actualDllPath);
                            actualDllPath = targetDllPath;
                        }
                        catch (Exception e)
                        {
                            BotLog.Error($"插件移动/删除出错: {pluginDisplayName} - {e.Message}");
                            throw;
                        }
                    }
                }
                catch (Exception e)
                {
                    BotLog.Error(e.Message);
                    return;
                }
            }

            if (loader is null)
            {
                loader = CreateLoader();
                var plugin = loader.Load(actualDllPath, pluginInfo.TypeFullName);

                lock (PluginLifecycleLock)
                {
                    if (IsPluginLoaded(pluginInfo.Id))
                    {
                        CH.Warning($"插件重复，已跳过：{pluginInfo.Id} ({actualDllPath})");
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
                    pluginInfo.Id,
                    pluginDirectory,
                    routePolicy);

                CH.Info($"开始加载插件: {pluginInfo.Name} v{pluginInfo.Version} ");

                using (BotLog.BeginScope(pluginContext.Logger))
                {
                    await plugin.OnLoad(pluginContext);
                }

                var pluginHandle = new LoadedPluginHandle(
                    plugin,
                    pluginContext,
                    loader,
                    actualDllPath,
                    pluginInfo,
                    CreateGroupRouteFilter(pluginInfo.Id, routePolicy));

                lock (PluginLifecycleLock)
                {
                    if (IsPluginLoaded(pluginInfo.Id))
                    {
                        CH.Warning($"插件重复，已跳过：{pluginInfo.Id} ({actualDllPath})");
                        loader.Unload();
                        return;
                    }

                    _loadedPlugins.Add(pluginHandle);
                    hostEventDispatcher.RegisterPlugin(pluginHandle);
                }

                CH.Success($"插件加载成功: {pluginInfo.Id} ({actualDllPath})");
                loader = null;
            }
        }
        catch (Exception ex)
        {
            CH.Error($"插件加载失败: {dll} - {ex.Message}");
            loader?.Unload();
        }
    }

    private bool IsPluginLoaded(string pluginId) =>
        _loadedPlugins.Any(item => string.Equals(item.Name, pluginId, StringComparison.OrdinalIgnoreCase));

    private DllLoader<IBotPlugin> CreateLoader() =>
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

            if (File.Exists(fullPath)) return [fullPath];

            if (Directory.Exists(fullPath))
            {
                return ResolvePluginDirectoryCandidates(fullPath, normalizedInput);
            }

            if (!Path.IsPathRooted(normalizedInput))
            {
                var pluginRelativePath = Path.GetFullPath(Path.Combine(pluginRoot, normalizedInput));
                if (File.Exists(pluginRelativePath)) return [pluginRelativePath];
                if (Directory.Exists(pluginRelativePath)) return ResolvePluginDirectoryCandidates(pluginRelativePath, normalizedInput);
            }

            return [];
        }

        foreach (var alias in GetPluginNameAliases(normalizedInput))
        {
            var directDll = Path.Combine(pluginRoot, $"{alias}.dll");
            if (File.Exists(directDll)) return [Path.GetFullPath(directDll)];

            var pluginDirectoryDll = Path.Combine(pluginRoot, alias, $"{alias}.dll");
            if (File.Exists(pluginDirectoryDll)) return [Path.GetFullPath(pluginDirectoryDll)];

            var pluginDirectory = Path.Combine(pluginRoot, alias);
            if (Directory.Exists(pluginDirectory)) return ResolvePluginDirectoryCandidates(pluginDirectory, normalizedInput);
        }

        var inputAliases = GetPluginNameAliases(normalizedInput).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return EnumerateLoadableCandidateAssemblies(pluginRoot)
            .Where(path =>
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                var directoryName = new DirectoryInfo(Path.GetDirectoryName(path) ?? pluginRoot).Name;
                var pluginName = TryProbePluginInfoCached(path)?.Id;
                return NameMatches(fileName, inputAliases) ||
                       (NameMatches(directoryName, inputAliases) &&
                        string.Equals(fileName, directoryName, StringComparison.OrdinalIgnoreCase)) ||
                       NameMatches(pluginName, inputAliases);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> ResolvePluginDirectoryCandidates(string pluginDirectory, string pluginNameOrPath)
    {
        var directoryName = new DirectoryInfo(pluginDirectory).Name;
        var inputName = Path.GetFileName(pluginNameOrPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var aliases = GetPluginNameAliases(inputName).Concat(GetPluginNameAliases(directoryName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sameNameDll = Path.Combine(pluginDirectory, $"{directoryName}.dll");
        if (File.Exists(sameNameDll)) return [Path.GetFullPath(sameNameDll)];

        var pluginDlls = Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(path =>
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (NameMatches(fileName, aliases)) return true;

                var pluginName = TryProbePluginInfoCached(path)?.Id;
                return NameMatches(pluginName, aliases);
            })
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pluginDlls.Count > 0) return pluginDlls;

        var loadableDlls = Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(path => TryProbePluginInfoCached(path) is not null)
            .Select(Path.GetFullPath)
            .ToList();

        return loadableDlls.Count == 1 ? loadableDlls : [];
    }

    private static IEnumerable<string> GetPluginNameAliases(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;

        yield return name;

        const string shiroBotPrefix = "ShiroBot.";
        if (name.StartsWith(shiroBotPrefix, StringComparison.OrdinalIgnoreCase) && name.Length > shiroBotPrefix.Length)
        {
            yield return name[shiroBotPrefix.Length..];
        }
    }

    private static bool NameMatches(string? candidate, HashSet<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (aliases.Contains(candidate)) return true;

        return candidate.StartsWith("ShiroBot.", StringComparison.OrdinalIgnoreCase) &&
               aliases.Contains(candidate["ShiroBot.".Length..]);
    }

    private PluginProbeInfo ProbePluginInfo(string assemblyPath) =>
        TryProbePluginInfoCached(assemblyPath)
        ?? throw new InvalidOperationException($"No {nameof(BotPluginAttribute)} found on IBotPlugin type in DLL");

    private PluginProbeInfo? TryProbePluginInfoCached(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            RemoveProbeCache(fullPath);
            return null;
        }

        var fileInfo = new FileInfo(fullPath);
        var dirty = _dirtyProbePaths.ContainsKey(fullPath);
        if (!dirty &&
            _probeCache.TryGetValue(fullPath, out var cached) &&
            cached.Length == fileInfo.Length &&
            cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
        {
            return cached.Info;
        }

        _dirtyProbePaths.TryRemove(fullPath, out _);
        var info = TryProbePluginInfo(fullPath);
        _probeCache[fullPath] = new PluginProbeCacheEntry(fileInfo.Length, fileInfo.LastWriteTimeUtc, info);
        return info;
    }

    private static PluginProbeInfo? TryProbePluginInfo(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return null;

            var reader = peReader.GetMetadataReader();
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                var attributes = type.Attributes;
                if ((attributes & TypeAttributes.Interface) != 0 || (attributes & TypeAttributes.Abstract) != 0)
                {
                    continue;
                }

                foreach (var attributeHandle in type.GetCustomAttributes())
                {
                    if (!TryReadBotPluginAttribute(reader, attributeHandle, out var pluginAttribute)) continue;

                    var id = pluginAttribute.Id;
                    if (string.IsNullOrWhiteSpace(id)) return null;

                    return new PluginProbeInfo(
                        GetTypeDefinitionFullName(reader, type),
                        id,
                        pluginAttribute.Name ?? id,
                        pluginAttribute.Version ?? "1.0.0",
                        pluginAttribute.Description,
                        pluginAttribute.GithubRepo,
                        pluginAttribute.IsPluginSingleFile ?? false);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadBotPluginAttribute(
        MetadataReader reader,
        CustomAttributeHandle attributeHandle,
        out RawBotPluginAttribute attribute)
    {
        attribute = default;

        var customAttribute = reader.GetCustomAttribute(attributeHandle);
        if (!string.Equals(
                GetAttributeTypeFullName(reader, customAttribute),
                typeof(BotPluginAttribute).FullName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var blob = reader.GetBlobReader(customAttribute.Value);
        if (blob.ReadUInt16() != 1) return false; // CustomAttribute prolog

        var id = blob.ReadSerializedString();
        if (string.IsNullOrWhiteSpace(id)) return false;

        string? name = null;
        string? version = null;
        string? description = null;
        string? githubRepo = null;
        bool? isPluginSingleFile = null;

        var namedArgumentCount = blob.ReadUInt16();
        for (var i = 0; i < namedArgumentCount; i++)
        {
            _ = blob.ReadByte(); // FIELD(0x53) or PROPERTY(0x54)
            var typeCode = blob.ReadByte();
            var memberName = blob.ReadSerializedString();

            switch (memberName)
            {
                case nameof(BotPluginAttribute.Name) when typeCode == SerializedTypeString:
                    name = blob.ReadSerializedString();
                    break;
                case nameof(BotPluginAttribute.Version) when typeCode == SerializedTypeString:
                    version = blob.ReadSerializedString();
                    break;
                case nameof(BotPluginAttribute.Description) when typeCode == SerializedTypeString:
                    description = blob.ReadSerializedString();
                    break;
                case nameof(BotPluginAttribute.GithubRepo) when typeCode == SerializedTypeString:
                    githubRepo = blob.ReadSerializedString();
                    break;
                case nameof(BotPluginAttribute.IsPluginSingleFile) when typeCode == SerializedTypeBoolean:
                    isPluginSingleFile = blob.ReadBoolean();
                    break;
                default:
                    if (!TrySkipSerializedFixedArgument(ref blob, typeCode)) return false;
                    break;
            }
        }

        attribute = new RawBotPluginAttribute(id, name, version, description, githubRepo, isPluginSingleFile);
        return true;
    }

    private static bool TrySkipSerializedFixedArgument(ref BlobReader blob, byte typeCode)
    {
        try
        {
            switch (typeCode)
            {
                case SerializedTypeBoolean:
                case SerializedTypeI1:
                case SerializedTypeU1:
                    blob.ReadByte();
                    return true;
                case SerializedTypeChar:
                case SerializedTypeI2:
                case SerializedTypeU2:
                    blob.ReadBytes(2);
                    return true;
                case SerializedTypeI4:
                case SerializedTypeU4:
                case SerializedTypeR4:
                    blob.ReadBytes(4);
                    return true;
                case SerializedTypeI8:
                case SerializedTypeU8:
                case SerializedTypeR8:
                    blob.ReadBytes(8);
                    return true;
                case SerializedTypeString:
                case SerializedTypeType:
                    blob.ReadSerializedString();
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string? GetAttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        return attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => GetMemberReferenceParentTypeFullName(reader, reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor)),
            HandleKind.MethodDefinition => GetTypeDefinitionFullName(reader, reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType())),
            _ => null
        };
    }

    private static string? GetMemberReferenceParentTypeFullName(MetadataReader reader, MemberReference memberReference)
    {
        return memberReference.Parent.Kind switch
        {
            HandleKind.TypeReference => GetTypeReferenceFullName(reader, reader.GetTypeReference((TypeReferenceHandle)memberReference.Parent)),
            HandleKind.TypeDefinition => GetTypeDefinitionFullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)memberReference.Parent)),
            _ => null
        };
    }

    private static string GetTypeDefinitionFullName(MetadataReader reader, TypeDefinition type)
    {
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string GetTypeReferenceFullName(MetadataReader reader, TypeReference type)
    {
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private const byte SerializedTypeBoolean = 0x02;
    private const byte SerializedTypeChar = 0x03;
    private const byte SerializedTypeI1 = 0x04;
    private const byte SerializedTypeU1 = 0x05;
    private const byte SerializedTypeI2 = 0x06;
    private const byte SerializedTypeU2 = 0x07;
    private const byte SerializedTypeI4 = 0x08;
    private const byte SerializedTypeU4 = 0x09;
    private const byte SerializedTypeI8 = 0x0A;
    private const byte SerializedTypeU8 = 0x0B;
    private const byte SerializedTypeR4 = 0x0C;
    private const byte SerializedTypeR8 = 0x0D;
    private const byte SerializedTypeString = 0x0E;
    private const byte SerializedTypeType = 0x50;

    private readonly record struct RawBotPluginAttribute(
        string Id,
        string? Name,
        string? Version,
        string? Description,
        string? GithubRepo,
        bool? IsPluginSingleFile);
}
