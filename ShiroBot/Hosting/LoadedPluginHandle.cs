using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting;

internal sealed class LoadedPluginHandle
{
    private readonly Lock _dispatchLock = new();
    private int _activeDispatches;
    private bool _isUnloading;
    private TaskCompletionSource? _dispatchesDrained;
    private Task<PluginUnloadResult>? _unloadTask;
    private IBotPlugin? _plugin;
    private PluginContext? _context;
    private DllLoader<IBotPlugin>? _loader;
    private readonly Func<long, bool>? _groupRouteFilter;
    private readonly string _assemblyPath;
    private readonly HostLogHub _logHub;

    public LoadedPluginHandle(
        IBotPlugin plugin,
        PluginContext context,
        DllLoader<IBotPlugin> loader,
        string assemblyPath,
        PluginProbeInfo metadata,
        HostLogHub logHub,
        Func<long, bool>? groupRouteFilter = null)
    {
        _plugin = plugin;
        _context = context;
        _loader = loader;
        _assemblyPath = assemblyPath;
        _logHub = logHub;
        _groupRouteFilter = groupRouteFilter;

        Name = metadata.Id;
        DisplayName = metadata.Name;
        Version = metadata.Version;
        Description = metadata.Description;
        Author = metadata.Author;
        Category = metadata.Category;
        GithubRepo = metadata.GithubRepo;
        SubscribedEventTypes = plugin is PluginBase pluginBase
            ? pluginBase.GetEffectiveEventTypes().ToHashSet()
            : new HashSet<Type>();
        GroupMessageRoutes = plugin is PluginBase groupPluginBase ? groupPluginBase.GetGroupMessageRoutes() : Array.Empty<MessageRouteDescriptor>();
        FriendMessageRoutes = plugin is PluginBase friendPluginBase ? friendPluginBase.GetFriendMessageRoutes() : Array.Empty<MessageRouteDescriptor>();
        RequiresGroupMessageBroadcast = plugin is PluginBase groupBroadcastPluginBase && groupBroadcastPluginBase.RequiresGroupMessageBroadcast();
        RequiresFriendMessageBroadcast = plugin is PluginBase friendBroadcastPluginBase && friendBroadcastPluginBase.RequiresFriendMessageBroadcast();
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string? Description { get; }
    public string? Author { get; }
    public PluginCategory Category { get; }
    public string? GithubRepo { get; }
    public string AssemblyPath => _assemblyPath;
    public IReadOnlySet<Type> SubscribedEventTypes { get; }
    public IReadOnlyList<MessageRouteDescriptor> GroupMessageRoutes { get; }
    public IReadOnlyList<MessageRouteDescriptor> FriendMessageRoutes { get; }
    public bool RequiresGroupMessageBroadcast { get; }
    public bool RequiresFriendMessageBroadcast { get; }

    public bool HandlesGroupMessagesViaBroadcast =>
        RequiresGroupMessageBroadcast ||
        (SubscribesTo(typeof(GroupIncomingMessage)) && GroupMessageRoutes.Count == 0);

    public bool HandlesFriendMessagesViaBroadcast =>
        RequiresFriendMessageBroadcast ||
        (SubscribesTo(typeof(FriendIncomingMessage)) && FriendMessageRoutes.Count == 0);

    public bool SubscribesTo(Type eventType) => SubscribedEventTypes.Contains(eventType);

    public bool AllowsGroup(long? groupId)
    {
        if (!groupId.HasValue || _groupRouteFilter is null)
        {
            return true;
        }

        return _groupRouteFilter(groupId.Value);
    }

    /// <summary>
    /// Gets the combined on-disk size of assembly files loaded into this plugin's load context.
    /// This is not the plugin's runtime memory usage.
    /// </summary>
    public long GetLoadedAssemblyFileBytes()
    {
        var loader = _loader;
        if (loader?.Alc is null)
        {
            return 0;
        }

        long totalBytes = 0;
        foreach (var assembly in loader.Alc.Assemblies)
        {
#pragma warning disable IL3000
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(assembly.Location);
#pragma warning restore IL3000
                if (fileInfo.Exists)
                {
                    totalBytes += fileInfo.Length;
                }
            }
            catch (Exception)
            {
                // The plugin may be unloading or the assembly file may no longer be readable.
            }
        }

        return totalBytes;
    }

    public bool Supports<THandler>()
        where THandler : class
    {
        lock (_dispatchLock)
        {
            return !_isUnloading && _plugin is THandler;
        }
    }

    public async Task<bool> DispatchAsync<THandler>(Func<THandler, Task> dispatch)
        where THandler : class
    {
        THandler handler;
        IConsoleLogger logger;
        lock (_dispatchLock)
        {
            if (_isUnloading || _plugin is not THandler candidate || _context?.Logger is not { } contextLogger)
            {
                return false;
            }

            handler = candidate;
            logger = contextLogger;
            _activeDispatches++;
        }

        try
        {
            await BotLog.RunScoped(logger, () => dispatch(handler));
            return true;
        }
        finally
        {
            CompleteDispatch();
        }
    }

    public async Task<PluginDispatchResult<TResult>> DispatchAsync<THandler, TResult>(
        Func<THandler, Task<TResult>> dispatch)
        where THandler : class
    {
        THandler handler;
        IConsoleLogger logger;
        lock (_dispatchLock)
        {
            if (_isUnloading || _plugin is not THandler candidate || _context?.Logger is not { } contextLogger)
            {
                return new PluginDispatchResult<TResult>(false, default);
            }

            handler = candidate;
            logger = contextLogger;
            _activeDispatches++;
        }

        try
        {
            using var _ = BotLog.BeginScope(logger);
            var result = await dispatch(handler);
            return new PluginDispatchResult<TResult>(true, result);
        }
        finally
        {
            CompleteDispatch();
        }
    }

    public Task<PluginUnloadResult> UnloadAsync()
    {
        lock (_dispatchLock)
        {
            if (_unloadTask is not null)
            {
                return _unloadTask;
            }

            _isUnloading = true;
            var dispatchesDrained = _activeDispatches == 0
                ? Task.CompletedTask
                : (_dispatchesDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;

            _unloadTask = UnloadWhenDispatchesDrainedAsync(dispatchesDrained);
            return _unloadTask;
        }
    }

    private void CompleteDispatch()
    {
        TaskCompletionSource? dispatchesDrained = null;
        lock (_dispatchLock)
        {
            _activeDispatches--;
            if (_activeDispatches == 0)
            {
                dispatchesDrained = _dispatchesDrained;
            }
        }

        dispatchesDrained?.TrySetResult();
    }

    private async Task<PluginUnloadResult> UnloadWhenDispatchesDrainedAsync(Task dispatchesDrained)
    {
        // UnloadAsync creates this task while holding _dispatchLock. Always yield once so plugin
        // cleanup code can never run under the lifecycle lock, even when no dispatch is active.
        await Task.Yield();
        await dispatchesDrained.ConfigureAwait(false);

        IBotPlugin? plugin;
        PluginContext? context;
        DllLoader<IBotPlugin>? loader;
        lock (_dispatchLock)
        {
            plugin = _plugin;
            context = _context;
            loader = _loader;

            _plugin = null;
            _context = null;
            _loader = null;
        }

        return await BeginUnloadCore(Name, _assemblyPath, plugin, context, loader, _logHub)
            .ConfigureAwait(false);
    }

    private static Task<PluginUnloadResult> BeginUnloadCore(
        string name,
        string assemblyPath,
        IBotPlugin? plugin,
        PluginContext? context,
        DllLoader<IBotPlugin>? loader,
        HostLogHub logHub)
    {
        var pluginWeakReference = plugin is null ? null : new WeakReference(plugin);
        var contextWeakReference = context is null ? null : new WeakReference(context);
        Exception? unloadException;
        IDisposable? scope = null;
        try
        {
            if (plugin is null)
            {
                throw new InvalidOperationException("Plugin is not available for unload.");
            }

            scope = BotLog.BeginScope(new ConsoleLogger($"[Plugin:{name}]", logHub));
            context?.DetachExternalCallbacks();
            ReleaseAvaloniaPluginResources(plugin.GetType().Assembly.GetName().Name);
            var unloadTask = plugin.OnUnload();

            if (!unloadTask.IsCompletedSuccessfully)
                return AwaitUnloadCoreAsync(
                    name,
                    assemblyPath,
                    pluginWeakReference,
                    contextWeakReference,
                    context,
                    loader,
                    unloadTask,
                    scope);
            scope.Dispose();
            context?.Dispose();
            var alcWeakReference = loader?.BeginUnload();

            return Task.FromResult(new PluginUnloadResult(
                name,
                assemblyPath,
                true,
                alcWeakReference,
                pluginWeakReference,
                contextWeakReference,
                null));

        }
        catch (Exception ex)
        {
            unloadException = ex;
        }
        finally
        {
            scope?.Dispose();
        }

        context?.Dispose();
        var failedAlcWeakReference = loader?.BeginUnload();

        return Task.FromResult(new PluginUnloadResult(
            name,
            assemblyPath,
            unloadException is null,
            failedAlcWeakReference,
            pluginWeakReference,
            contextWeakReference,
            unloadException));
    }

    private static async Task<PluginUnloadResult> AwaitUnloadCoreAsync(
        string name,
        string assemblyPath,
        WeakReference? pluginWeakReference,
        WeakReference? contextWeakReference,
        PluginContext? context,
        DllLoader<IBotPlugin>? loader,
        Task unloadTask,
        IDisposable scope)
    {
        Exception? unloadException = null;
        try
        {
            await unloadTask;
        }
        catch (Exception ex)
        {
            unloadException = ex;
        }
        finally
        {
            scope.Dispose();
            context?.Dispose();
        }

        var alcWeakReference = loader?.BeginUnload();

        return new PluginUnloadResult(
            name,
            assemblyPath,
            unloadException is null,
            alcWeakReference,
            pluginWeakReference,
            contextWeakReference,
            unloadException);
    }

    private static void ReleaseAvaloniaPluginResources(string? assemblyName)
    {
        AvaloniaIntegration.AvaloniaIntegration.ReleasePluginAssembly(assemblyName);
    }
}

internal sealed record PluginUnloadResult(
    string Name,
    string AssemblyPath,
    bool Unloaded,
    WeakReference? AssemblyLoadContextWeakReference,
    WeakReference? PluginWeakReference,
    WeakReference? ContextWeakReference,
    Exception? Error);

internal sealed record PluginDispatchResult<TResult>(bool Dispatched, TResult? Result);
