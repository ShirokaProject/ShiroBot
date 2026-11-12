using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Adapters;

internal sealed partial class AdapterManager
{
    public async Task<IReadOnlyList<PluginActionDescriptor>> GetActionsAsync(string id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return ReadActions(FindActionProvider(id)); }
        finally { _gate.Release(); }
    }

    public async Task<PluginActionResult> ExecuteActionAsync(string id, string actionId, CancellationToken token = default)
    {
        // Serialize with stop/reload so an action cannot retain an unloading adapter.
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var provider = FindActionProvider(id);
            if (!ReadActions(provider).Any(x => x.Id == actionId))
                throw new KeyNotFoundException($"Adapter {id} 未提供操作: {actionId}");
            using var scope = AdapterExecutionContext.Enter(id);
            return await provider!.ExecuteActionAsync(actionId, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private IPluginActionProvider? FindActionProvider(string id)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(id, out var entry) || entry.Adapter is null)
                throw new KeyNotFoundException($"Adapter 实例未运行: {id}");
            return entry.Adapter as IPluginActionProvider;
        }
    }

    private static IReadOnlyList<PluginActionDescriptor> ReadActions(IPluginActionProvider? provider) =>
        provider?.Actions.Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Label))
            .DistinctBy(x => x.Id).ToArray() ?? [];
}
