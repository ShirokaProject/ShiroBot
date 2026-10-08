using ShiroBot.Configuration;
using ShiroBot.Adapters;
using ShiroBot.Hosting.Events;
using ShiroBot.Plugins;

namespace ShiroBot.Components.Reloading;

internal sealed class ComponentReloadCoordinator(
    AdapterManager adapterManager,
    PluginManager pluginManager,
    HostEventDispatcher eventDispatcher,
    PluginRouteConfig routePolicy)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ReloadAdapterAsync(string? assemblyPath = null)
    {
        await ExecuteAdapterMutationAsync(
            () => adapterManager.ReloadAsync(assemblyPath)).ConfigureAwait(false);
    }

    public Task ReloadAdapterByIdAsync(string id) =>
        ExecuteAdapterMutationAsync(() => adapterManager.ReloadByIdAsync(id));

    public async Task ExecuteAdapterMutationAsync(Func<Task> mutation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        IReadOnlyList<string> plugins = [];
        try
        {
            plugins = await pluginManager.UnloadAllAsync(eventDispatcher).ConfigureAwait(false);
            await mutation().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await pluginManager.ReloadAsync(eventDispatcher, routePolicy, plugins).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// Starts or reconfigures adapters without replacing a loaded assembly. Plugins can only hold references to
    /// adapters that are already running, so nothing has to be unloaded first.
    /// </summary>
    public Task ExecuteAdapterStartAsync(Func<Task> mutation) =>
        ExecuteAdapterStartAsync(async () => { await mutation().ConfigureAwait(false); return true; });

    public async Task<T> ExecuteAdapterStartAsync<T>(Func<Task<T>> mutation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await mutation().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<T> ExecuteAdapterMutationAsync<T>(Func<Task<T>> mutation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        IReadOnlyList<string> plugins = [];
        try
        {
            plugins = await pluginManager.UnloadAllAsync(eventDispatcher).ConfigureAwait(false);
            return await mutation().ConfigureAwait(false);
        }
        finally
        {
            try { await pluginManager.ReloadAsync(eventDispatcher, routePolicy, plugins).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
    }

}
