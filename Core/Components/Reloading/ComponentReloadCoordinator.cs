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
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var plugins = await pluginManager.UnloadAllAsync(eventDispatcher).ConfigureAwait(false);
            var previousAdapterPaths = adapterManager.AssemblyPaths;
            try
            {
                await adapterManager.ReloadAsync(assemblyPath).ConfigureAwait(false);
            }
            catch
            {
                if (!adapterManager.IsLoaded && previousAdapterPaths.Count > 0)
                {
                    try
                    {
                        await adapterManager.LoadAsync(previousAdapterPaths.Where(File.Exists)).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Keep the original reload error; runtime state already shows no adapter.
                    }
                }

                throw;
            }
            finally
            {
                await pluginManager.ReloadAsync(eventDispatcher, routePolicy, plugins).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

}
