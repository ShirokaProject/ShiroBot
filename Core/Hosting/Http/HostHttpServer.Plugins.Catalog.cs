using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Context;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Marketplace;
using ShiroBot.Update;
using ShiroBot.Console;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static void MapPluginCatalogEndpoints(RouteGroupBuilder api, PluginManager pluginManager)
    {
        api.MapGet("/plugins/list", () => Results.Ok(GetPluginListItems(pluginManager)));

        api.MapGet("/plugin-market/plugins", async (HttpContext context) =>
        {
            try
            {
                var forceRefresh = context.Request.Query.TryGetValue("refresh", out var refreshValue) &&
                                   (refreshValue == "1" || string.Equals(refreshValue, "true", StringComparison.OrdinalIgnoreCase));
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
                "enabled"))
            .ToArray();

        var enabledIds = enabledPlugins.Select(plugin => plugin.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disabledPlugins = EnumerateDisabledPlugins(pluginManager)
            .Where(plugin => !enabledIds.Contains(plugin.Id))
            .ToArray();
        var listedIds = enabledIds.Concat(disabledPlugins.Select(plugin => plugin.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unloadedPlugins = EnumerateUnloadedPlugins(pluginManager)
            .Where(plugin => !listedIds.Contains(plugin.Id))
            .ToArray();

        return enabledPlugins.Concat(disabledPlugins).Concat(unloadedPlugins).ToArray();
    }

    internal static PluginListItem? FindPluginListItem(PluginManager pluginManager, string id) =>
        GetPluginListItems(pluginManager).FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
}
