using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting;

internal sealed class LoadedPluginHandle
{
    private int _state;
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
        Subscriptions = plugin is PluginBase pluginBase ? pluginBase.GetEffectiveSubscriptions() : BotEventSubscriptions.None;
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
    public BotEventSubscriptions Subscriptions { get; }
    public IReadOnlyList<MessageRouteDescriptor> GroupMessageRoutes { get; }
    public IReadOnlyList<MessageRouteDescriptor> FriendMessageRoutes { get; }
    public bool RequiresGroupMessageBroadcast { get; }
    public bool RequiresFriendMessageBroadcast { get; }

    public bool HandlesGroupMessagesViaBroadcast =>
        RequiresGroupMessageBroadcast ||
        ((Subscriptions & BotEventSubscriptions.GroupMessage) != 0 && GroupMessageRoutes.Count == 0);

    public bool HandlesFriendMessagesViaBroadcast =>
        RequiresFriendMessageBroadcast ||
        ((Subscriptions & BotEventSubscriptions.FriendMessage) != 0 && FriendMessageRoutes.Count == 0);

    public bool AllowsGroup(long? groupId)
    {
        if (!groupId.HasValue || _groupRouteFilter is null)
        {
            return true;
        }

        return _groupRouteFilter(groupId.Value);
    }

    public long GetLoadedAssemblyBytes()
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
        var plugin = _plugin;
        return plugin is THandler;
    }

    public async Task<bool> DispatchAsync<THandler>(Func<THandler, Task> dispatch)
        where THandler : class
    {
        var plugin = _plugin;
        if (plugin is not THandler handler)
        {
            return false;
        }

        var logger = _context?.Logger;
        if (logger is null)
        {
            return false;
        }

        await BotLog.RunScoped(logger, () => dispatch(handler));
        return true;
    }

    public Task<PluginUnloadResult> UnloadAsync()
    {
        if (Interlocked.Exchange(ref _state, 1) == 1)
        {
            return Task.FromResult(new PluginUnloadResult(Name, _assemblyPath, true, null, null, null, null));
        }

        var plugin = _plugin;
        var context = _context;
        var loader = _loader;

        _plugin = null;
        _context = null;
        _loader = null;

        return BeginUnloadCore(Name, _assemblyPath, plugin, context, loader, _logHub);
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
#if AVALONIA
        AvaloniaIntegration.AvaloniaIntegration.ReleasePluginAssembly(assemblyName);
#else
        _ = assemblyName;
#endif
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
