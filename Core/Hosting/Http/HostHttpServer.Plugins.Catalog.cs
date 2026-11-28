using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Plugins;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    internal sealed class PluginCatalogEndpoints(PluginManager pluginManager) : IHostApiEndpoints
    {
        public void Map(RouteGroupBuilder api)
        {
            api.MapGet("/plugins/contract-abi", () => Results.Ok(PluginManager.GetContractAbiVersions()));
            api.MapGet("/plugins/list", () => Results.Ok(GetPluginListItems(pluginManager)));

            api.MapGet("/plugin-market/plugins", async (HttpContext context) =>
            {
                try
                {
                    var forceRefresh = context.Request.Query.TryGetValue("refresh", out var refreshValue) &&
                                       (refreshValue == "1" || string.Equals(refreshValue, "true",
                                           StringComparison.OrdinalIgnoreCase));
                    var marketplace = await MarketplaceCache.GetAsync(
                        GetMarketplaceInstalledPlugins(pluginManager),
                        context.RequestAborted,
                        forceRefresh).ConfigureAwait(false);
                    return Results.Json(marketplace);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return Results.StatusCode(499);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Json(
                        new { error = "marketplace_unavailable", message = ex.Message },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });
        }
    }

    private static PluginListItem[] GetPluginListItems(PluginManager pluginManager)
    {
        var enabledPlugins = pluginManager.GetLoadedPluginSnapshot()
            .Select(plugin => new PluginListItem(
                plugin.Name,
                plugin.DisplayName,
                plugin.Version,
                true,
                string.IsNullOrWhiteSpace(plugin.Author) ? "Unknown" : plugin.Author,
                plugin.GithubRepo,
                plugin.Description ?? string.Empty,
                plugin.Category.ToString(),
                "enabled",
                Priority: plugin.Priority))
            .ToArray();

        var enabledIds = enabledPlugins.Select(plugin => plugin.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disabledPlugins = EnumerateDisabledPlugins(pluginManager)
            .Where(plugin => !enabledIds.Contains(plugin.Id))
            .ToArray();
        var listedIds = enabledIds.Concat(disabledPlugins.Select(plugin => plugin.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unloadedPlugins = EnumerateUnloadedPlugins(pluginManager)
            .Where(plugin => !listedIds.Contains(plugin.Id))
            .ToArray();

        return [.. enabledPlugins.Concat(disabledPlugins).Concat(unloadedPlugins)];
    }

    internal static PluginListItem? FindPluginListItem(PluginManager pluginManager, string id)
    {
        return GetPluginListItems(pluginManager).FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}