using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins;

/// <summary>
/// Reads plugin-declared dashboard tabs. A plugin that does not implement the optional
/// provider simply has no custom tabs; a throwing provider is logged and left out.
/// </summary>
internal static class PluginDashboardTabs
{
    public static async Task<IReadOnlyList<DashboardTabDescriptor>?> ListAsync(LoadedPluginHandle plugin)
    {
        if (!plugin.Supports<IPluginDashboardTabProvider>()) return [];

        var dispatch = await plugin.DispatchAsync<IPluginDashboardTabProvider, IReadOnlyList<DashboardTabDescriptor>>(
            provider => Task.FromResult(provider.Tabs)).ConfigureAwait(false);
        if (!dispatch.Dispatched) return null;

        return (dispatch.Result ?? [])
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Id) && !string.IsNullOrWhiteSpace(tab.Label))
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Source?.Path))
            .Where(tab => tab.Kind == DashboardTabKinds.Link ||
                          (tab.Source?.Path.StartsWith("/plugin/", StringComparison.OrdinalIgnoreCase) ?? false))
            .GroupBy(tab => tab.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(tab => tab.Order)
            .ToArray();
    }
}
