using System.Runtime.CompilerServices;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Dashboard;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Adapters;
internal sealed partial class AdapterManager
{
    private static readonly ConditionalWeakTable<IBotAdapter, AdapterDashboardOwner> DashboardOwners = new();
    internal IDashboardOwner? GetDashboardOwner(string id)
    {
        lock (_sync)
            return _entries.TryGetValue(id, out var entry) && entry.Adapter is { } adapter &&
                DashboardOwners.TryGetValue(adapter, out var owner) ? owner : null;
    }

    private static void RegisterDashboard(IBotAdapter adapter, PluginLifetimeScope lifetime)
    {
        if (adapter.GetExtension<Action<IPluginDashboard>>() is not { } register) return;
        var dashboard = new PluginDashboardScope(async work =>
        {
            if (lifetime.Stopping.IsCancellationRequested) return false;
            try { await lifetime.Run(_ => work()).ConfigureAwait(false); return true; }
            catch (ObjectDisposedException) when (lifetime.Stopping.IsCancellationRequested) { return false; }
        }, lifetime);
        DashboardOwners.Remove(adapter);
        DashboardOwners.Add(adapter, new AdapterDashboardOwner(adapter.GetType().Name, dashboard));
        register(dashboard);
    }
    private sealed record AdapterDashboardOwner(string Name, PluginDashboardScope? Dashboard) : IDashboardOwner;
}
