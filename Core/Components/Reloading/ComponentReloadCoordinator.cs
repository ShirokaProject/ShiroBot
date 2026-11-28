using ShiroBot.Adapters;

namespace ShiroBot.Components.Reloading;

/// <summary>
///     Serializes adapter operations without changing plugin lifecycles. Plugin contexts route through
///     BotContext to the current instance; cached adapter extensions can instead require a host restart.
/// </summary>
internal sealed class ComponentReloadCoordinator(AdapterManager adapterManager)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task ReloadAdapterAsync(string? assemblyPath = null)
    {
        return ExecuteAdapterMutationAsync(() => adapterManager.ReloadAsync(assemblyPath));
    }

    /// <summary>Reloads the instances of a watched adapter DLL only when the file really changed.</summary>
    public Task<bool> ReloadChangedAdapterAsync(string assemblyPath)
    {
        return ExecuteAdapterMutationAsync(() => adapterManager.ReloadChangedAsync(assemblyPath));
    }

    public Task ReloadAdapterByIdAsync(string id)
    {
        return ExecuteAdapterMutationAsync(() => adapterManager.ReloadByIdAsync(id));
    }

    public Task ExecuteAdapterMutationAsync(Func<Task> mutation)
    {
        return ExecuteAdapterMutationAsync(async () =>
        {
            await mutation().ConfigureAwait(false);
            return true;
        });
    }

    public async Task<T> ExecuteAdapterMutationAsync<T>(Func<Task<T>> mutation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await mutation().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
