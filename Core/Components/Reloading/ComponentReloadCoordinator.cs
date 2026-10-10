using ShiroBot.Adapters;

namespace ShiroBot.Components.Reloading;

/// <summary>
/// Serializes adapter operations without changing plugin lifecycles. Plugin contexts route through
/// BotContext to the current instance; cached adapter extensions can instead require a host restart.
/// </summary>
internal sealed class ComponentReloadCoordinator(AdapterManager adapterManager)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task ReloadAdapterAsync(string? assemblyPath = null) =>
        ExecuteAdapterMutationAsync(() => adapterManager.ReloadAsync(assemblyPath));

    public Task ReloadAdapterByIdAsync(string id) =>
        ExecuteAdapterMutationAsync(() => adapterManager.ReloadByIdAsync(id));

    public Task ExecuteAdapterMutationAsync(Func<Task> mutation) =>
        ExecuteAdapterMutationAsync(async () => { await mutation().ConfigureAwait(false); return true; });

    public async Task<T> ExecuteAdapterMutationAsync<T>(Func<Task<T>> mutation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await mutation().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public Task ExecuteAdapterStartAsync(Func<Task> mutation) => ExecuteAdapterMutationAsync(mutation);
    public Task<T> ExecuteAdapterStartAsync<T>(Func<Task<T>> mutation) => ExecuteAdapterMutationAsync(mutation);
}
