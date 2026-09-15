using ShiroBot.Hosting.Runtime;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Logging;
using ShiroBot.Plugins.Loading;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins;

internal sealed class LoadedPluginHandle
{
    private static readonly TimeSpan DefaultActiveDispatchDrainTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPluginOnUnloadTimeout = TimeSpan.FromSeconds(30);

    private readonly Lock _dispatchLock = new();
    private readonly TimeSpan _activeDispatchDrainTimeout;
    private readonly TimeSpan _pluginOnUnloadTimeout;
    private int _activeDispatches;
    private bool _isUnloading;
    private TaskCompletionSource? _dispatchesDrained;
    private Task<PluginUnloadResult>? _unloadTask;
    private IBotPlugin? _plugin;
    private PluginContext? _context;
    private DllLoader<IBotPlugin>? _loader;
    private readonly Func<string, bool>? _groupRouteFilter;
    private readonly string _assemblyPath;
    private readonly HostLogHub _logHub;

    public LoadedPluginHandle(
        IBotPlugin plugin,
        PluginContext context,
        DllLoader<IBotPlugin> loader,
        string assemblyPath,
        PluginProbeInfo metadata,
        HostLogHub logHub,
        Func<string, bool>? groupRouteFilter = null,
        TimeSpan? activeDispatchDrainTimeout = null,
        TimeSpan? pluginOnUnloadTimeout = null)
    {
        _plugin = plugin;
        _context = context;
        _loader = loader;
        _assemblyPath = assemblyPath;
        _logHub = logHub;
        _groupRouteFilter = groupRouteFilter;
        _activeDispatchDrainTimeout = activeDispatchDrainTimeout ?? DefaultActiveDispatchDrainTimeout;
        _pluginOnUnloadTimeout = pluginOnUnloadTimeout ?? DefaultPluginOnUnloadTimeout;

        Name = metadata.Id;
        DisplayName = metadata.Name;
        Version = metadata.Version;
        Description = metadata.Description;
        Author = metadata.Author;
        Category = metadata.Category;
        GithubRepo = metadata.GithubRepo;
        Dependencies = metadata.Dependencies;
        SubscribedEventTypes = plugin is PluginBase pluginBase
            ? pluginBase.GetEffectiveEventTypes().ToHashSet()
            : plugin is IBotEventSubscriber
                ? [typeof(BotEvent)]
                : [];
        GroupMessageRoutes = plugin is PluginBase groupPluginBase ? groupPluginBase.GetGroupMessageRoutes() : Array.Empty<MessageRouteDescriptor>();
        DirectMessageRoutes = plugin is PluginBase directPluginBase ? directPluginBase.GetDirectMessageRoutes() : Array.Empty<MessageRouteDescriptor>();
        RequiresGroupMessageBroadcast = plugin is PluginBase groupBroadcastPluginBase && groupBroadcastPluginBase.RequiresGroupMessageBroadcast();
        RequiresDirectMessageBroadcast = plugin is PluginBase directBroadcastPluginBase && directBroadcastPluginBase.RequiresDirectMessageBroadcast();
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string? Description { get; }
    public string? Author { get; }
    public PluginCategory Category { get; }
    public string? GithubRepo { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public string AssemblyPath => _assemblyPath;
    public IReadOnlySet<Type> SubscribedEventTypes { get; }
    public IReadOnlyList<MessageRouteDescriptor> GroupMessageRoutes { get; }
    public IReadOnlyList<MessageRouteDescriptor> DirectMessageRoutes { get; }
    public bool RequiresGroupMessageBroadcast { get; }
    public bool RequiresDirectMessageBroadcast { get; }

    public bool HandlesGroupMessagesViaBroadcast =>
        RequiresGroupMessageBroadcast ||
        (SubscribesTo(typeof(MessageEvent)) && GroupMessageRoutes.Count == 0);

    public bool HandlesDirectMessagesViaBroadcast =>
        RequiresDirectMessageBroadcast ||
        (SubscribesTo(typeof(MessageEvent)) && DirectMessageRoutes.Count == 0);

    public bool SubscribesTo(Type eventType) =>
        SubscribedEventTypes.Any(subscribedType => subscribedType.IsAssignableFrom(eventType));

    public bool AllowsGroup(string? groupId)
    {
        if (groupId is null || _groupRouteFilter is null)
        {
            return true;
        }

        return _groupRouteFilter(groupId);
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

        var pluginWeakReference = plugin is null ? null : new WeakReference(plugin);
        var contextWeakReference = context is null ? null : new WeakReference(context);

        try
        {
            context?.DetachExternalCallbacks();
        }
        catch (Exception ex)
        {
            if (!dispatchesDrained.IsCompleted)
            {
                _ = CompleteFailedUnloadAfterDispatchesDrainAsync(
                    dispatchesDrained,
                    Name,
                    _assemblyPath,
                    pluginWeakReference,
                    contextWeakReference,
                    context,
                    loader,
                    ex);
                return new PluginUnloadResult(
                    Name,
                    _assemblyPath,
                    false,
                    null,
                    pluginWeakReference,
                    contextWeakReference,
                    ex);
            }

            return CompleteFailedUnload(
                Name,
                _assemblyPath,
                pluginWeakReference,
                contextWeakReference,
                context,
                loader,
                ex);
        }

        if (await Task.WhenAny(dispatchesDrained, Task.Delay(_activeDispatchDrainTimeout)).ConfigureAwait(false) != dispatchesDrained)
        {
            _ = CompleteUnloadAfterDispatchesDrainAsync(dispatchesDrained, plugin, context, loader);
            return CreateTimeoutResult(
                pluginWeakReference,
                contextWeakReference,
                $"Timed out after {_activeDispatchDrainTimeout} waiting for active plugin dispatches to finish. " +
                "The plugin remains resident and the host must be restarted to guarantee cleanup.");
        }

        return await UnloadPluginCoreAsync(
                plugin,
                context,
                loader,
                pluginWeakReference,
                contextWeakReference)
            .ConfigureAwait(false);
    }

    private async Task CompleteUnloadAfterDispatchesDrainAsync(
        Task dispatchesDrained,
        IBotPlugin? plugin,
        PluginContext? context,
        DllLoader<IBotPlugin>? loader)
    {
        await dispatchesDrained.ConfigureAwait(false);
        await UnloadPluginCoreAsync(
                plugin,
                context,
                loader,
                plugin is null ? null : new WeakReference(plugin),
                context is null ? null : new WeakReference(context))
            .ConfigureAwait(false);
    }

    private async Task<PluginUnloadResult> UnloadPluginCoreAsync(
        IBotPlugin? plugin,
        PluginContext? context,
        DllLoader<IBotPlugin>? loader,
        WeakReference? pluginWeakReference,
        WeakReference? contextWeakReference)
    {
        try
        {
            if (plugin is null)
            {
                throw new InvalidOperationException("Plugin is not available for unload.");
            }

            ReleaseAvaloniaPluginResources(plugin.GetType().Assembly.GetName().Name);
            var unloadTask = Task.Run(() =>
                BotLog.RunScoped(
                    new ConsoleLogger($"[Plugin:{Name}]", _logHub),
                    plugin.OnUnload));

            if (await Task.WhenAny(unloadTask, Task.Delay(_pluginOnUnloadTimeout)).ConfigureAwait(false) != unloadTask)
            {
                _ = CompleteCleanupAfterOnUnloadAsync(unloadTask, context, loader);
                return CreateTimeoutResult(
                    pluginWeakReference,
                    contextWeakReference,
                    $"Timed out after {_pluginOnUnloadTimeout} waiting for plugin OnUnload to finish. " +
                    "The plugin remains resident and the host must be restarted to guarantee cleanup.");
            }

            await unloadTask.ConfigureAwait(false);
            context?.Dispose();
            var alcWeakReference = loader?.BeginUnload();

            return new PluginUnloadResult(
                Name,
                _assemblyPath,
                true,
                alcWeakReference,
                pluginWeakReference,
                contextWeakReference,
                null);
        }
        catch (Exception ex)
        {
            return CompleteFailedUnload(
                Name,
                _assemblyPath,
                pluginWeakReference,
                contextWeakReference,
                context,
                loader,
                ex);
        }
    }

    private static async Task CompleteCleanupAfterOnUnloadAsync(
        Task unloadTask,
        PluginContext? context,
        DllLoader<IBotPlugin>? loader)
    {
        try
        {
            await unloadTask.ConfigureAwait(false);
        }
        catch
        {
            // The manager already reported the timeout. Cleanup can proceed once plugin code stops.
        }

        context?.Dispose();
        loader?.BeginUnload();
    }

    private static async Task CompleteFailedUnloadAfterDispatchesDrainAsync(
        Task dispatchesDrained,
        string name,
        string assemblyPath,
        WeakReference? pluginWeakReference,
        WeakReference? contextWeakReference,
        PluginContext? context,
        DllLoader<IBotPlugin>? loader,
        Exception unloadException)
    {
        await dispatchesDrained.ConfigureAwait(false);
        CompleteFailedUnload(
            name,
            assemblyPath,
            pluginWeakReference,
            contextWeakReference,
            context,
            loader,
            unloadException);
    }

    private PluginUnloadResult CreateTimeoutResult(
        WeakReference? pluginWeakReference,
        WeakReference? contextWeakReference,
        string message) =>
        new(
            Name,
            _assemblyPath,
            false,
            null,
            pluginWeakReference,
            contextWeakReference,
            new TimeoutException(message));

    private static PluginUnloadResult CompleteFailedUnload(
        string name,
        string assemblyPath,
        WeakReference? pluginWeakReference,
        WeakReference? contextWeakReference,
        PluginContext? context,
        DllLoader<IBotPlugin>? loader,
        Exception unloadException)
    {
        try
        {
            context?.Dispose();
        }
        catch
        {
            // Preserve the original unload failure.
        }

        var alcWeakReference = loader?.BeginUnload();

        return new PluginUnloadResult(
            name,
            assemblyPath,
            false,
            alcWeakReference,
            pluginWeakReference,
            contextWeakReference,
            unloadException);
    }

    private static void ReleaseAvaloniaPluginResources(string? assemblyName)
    {
        AvaloniaIntegration.ReleasePluginAssembly(assemblyName);
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
