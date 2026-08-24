using ShiroBot.Core;

namespace ShiroBot.Hosting;

internal sealed class ComponentReloadCoordinator(
    ModelPackageRegistry modelPackages,
    AdapterManager adapterManager,
    PluginManager pluginManager,
    HostEventDispatcher eventDispatcher,
    PluginRouteConfig routePolicy,
    HostRuntimeState runtimeState)
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

    public async Task ReloadModelsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var plugins = await pluginManager.UnloadAllAsync(eventDispatcher).ConfigureAwait(false);
            var hadAdapter = adapterManager.IsLoaded;
            var adapterPaths = adapterManager.AssemblyPaths;
            try
            {
                if (hadAdapter) await adapterManager.StopAsync().ConfigureAwait(false);
                await modelPackages.ReloadAsync().ConfigureAwait(false);
                runtimeState.SetModelsCount(modelPackages.GetPackages().Count);
                if (hadAdapter) await adapterManager.LoadAsync(adapterPaths.Where(File.Exists)).ConfigureAwait(false);
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

    public async Task InstallModelAsync(string stagedAssemblyPath)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var plugins = await pluginManager.UnloadAllAsync(eventDispatcher).ConfigureAwait(false);
            var hadAdapter = adapterManager.IsLoaded;
            var adapterPaths = adapterManager.AssemblyPaths;
            try
            {
                if (hadAdapter) await adapterManager.StopAsync().ConfigureAwait(false);
                await modelPackages.InstallAsync(stagedAssemblyPath).ConfigureAwait(false);
                runtimeState.SetModelsCount(modelPackages.GetPackages().Count);
                if (hadAdapter) await adapterManager.LoadAsync(adapterPaths.Where(File.Exists)).ConfigureAwait(false);
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
