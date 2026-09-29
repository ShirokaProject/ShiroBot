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
    private static void MapPluginManagementEndpoints(RouteGroupBuilder api, ApiHostConfig config, ConfigManager configManager, string configPath, PluginManager pluginManager, HostEventDispatcher eventDispatcher, PluginRouteConfig routePolicy)
    {
        api.MapGet("/plugins/{id}", (string id) =>
        {
            var plugin = FindPluginListItem(pluginManager, id);
            if (plugin is null)
            {
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到插件: {id}" });
            }

            return Results.Ok(new
            {
                id = plugin.Id,
                name = plugin.Name,
                version = plugin.Version,
                enabled = plugin.Enable,
                author = plugin.Author,
                repo = plugin.Repo,
                description = plugin.Description,
                category = plugin.Category
            });
        });

        api.MapGet("/plugins/{id}/config", (string id) =>
        {
            var plugin = FindPluginForConfig(pluginManager, id);
            if (plugin is null)
            {
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到插件: {id}" });
            }

            var pluginConfigPath = GetPluginConfigPath(pluginManager, plugin.AssemblyPath, plugin.Id);
            EnsureKnownPluginConfig(id, pluginConfigPath);
            return Results.Ok(new
            {
                plugin_id = plugin.Id,
                config = LoadTomlObject(pluginConfigPath),
                schema = GetPluginConfigSchema(plugin.AssemblyPath),
                routes = CreatePluginRouteResponse(routePolicy, plugin.Id)
            });
        });

        api.MapGet("/plugins/{id}/actions", async (string id, HttpContext context) =>
        {
            if (!IsBearerAuthorized(context, config))
            {
                return Results.Json(new ApiError("unauthorized", "A Bearer API key is required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var plugin = FindLoadedPlugin(pluginManager, id);
            if (plugin is null)
            {
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            }

            if (!plugin.Supports<IPluginActionProvider>())
            {
                return Results.Ok(new { actions = Array.Empty<object>() });
            }

            try
            {
                var dispatch = await plugin.DispatchAsync<IPluginActionProvider, IReadOnlyList<PluginActionDescriptor>>(
                    provider => Task.FromResult(provider.Actions)).ConfigureAwait(false);
                if (!dispatch.Dispatched)
                {
                    return Results.Conflict(new { error = "plugin_unloading", message = $"插件 {id} 正在卸载。" });
                }

                var actions = (dispatch.Result ?? [])
                    .Where(action => !string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(action.Label))
                    .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Select(action => new
                    {
                        id = action.Id,
                        label = action.Label,
                        description = action.Description,
                        tone = NormalizePluginActionTone(action.Tone),
                        requires_confirmation = action.RequiresConfirmation,
                        confirmation_text = action.ConfirmationText
                    })
                    .ToArray();

                return Results.Ok(new { actions });
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex)
            {
                BotLog.Error($"读取插件 Dashboard actions 失败: {plugin.Name} - {ex.Message}");
                return Results.Json(
                    new { error = "action_provider_failed", message = "插件操作列表读取失败。" },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        api.MapPost("/plugins/{id}/actions/{actionId}", async (string id, string actionId, HttpContext context) =>
        {
            if (!IsBearerAuthorized(context, config))
            {
                return Results.Json(new ApiError("unauthorized", "A Bearer API key is required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var plugin = FindLoadedPlugin(pluginManager, id);
            if (plugin is null || !plugin.Supports<IPluginActionProvider>())
            {
                return Results.NotFound(new { error = "action_not_found", message = $"插件 {id} 未提供该操作。" });
            }

            try
            {
                var dispatch = await plugin.DispatchAsync<IPluginActionProvider, PluginActionExecution>(
                    async provider =>
                    {
                        var descriptor = provider.Actions.FirstOrDefault(action =>
                            string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));
                        if (descriptor is null)
                        {
                            return new PluginActionExecution(false, null);
                        }

                        var actionResult = await provider.ExecuteActionAsync(actionId, context.RequestAborted)
                            .ConfigureAwait(false);
                        return new PluginActionExecution(true, actionResult);
                    }).ConfigureAwait(false);

                if (!dispatch.Dispatched)
                {
                    return Results.Conflict(new { error = "plugin_unloading", message = $"插件 {id} 正在卸载。" });
                }

                if (dispatch.Result is not { Found: true, Result: { } result })
                {
                    return Results.NotFound(new { error = "action_not_found", message = $"未找到插件操作: {actionId}" });
                }

                return Results.Ok(new { ok = result.Ok, message = result.Message, refresh = result.Refresh });
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex)
            {
                BotLog.Error($"执行插件 Dashboard action 失败: {plugin.Name}/{actionId} - {ex.Message}");
                return Results.Json(
                    new { ok = false, message = "插件操作执行失败。", refresh = false },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        api.MapPatch("/plugins/{id}/config", async (string id, HttpContext context) =>
        {
            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json", message = "插件配置更新内容不是有效 JSON。" });
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Results.BadRequest(new { error = "invalid_request", message = "插件配置更新内容必须是对象。" });
                }

                var plugin = FindPluginForConfig(pluginManager, id);
                if (plugin is null)
                {
                    return Results.NotFound(new { error = "plugin_not_found", message = $"未找到插件: {id}" });
                }

                try
                {
                    var pluginConfigPath = GetPluginConfigPath(pluginManager, plugin.AssemblyPath, plugin.Id);
                    EnsureKnownPluginConfig(id, pluginConfigPath);

                    if (document.RootElement.TryGetProperty("config", out var configPatch))
                    {
                        ApplyPluginConfigPatch(configManager, pluginConfigPath, plugin.Id, configPatch);
                    }

                    if (document.RootElement.TryGetProperty("routes", out var routePatch))
                    {
                        ApplyPluginRoutePatch(configManager, configPath, routePolicy, plugin.Id, routePatch);
                    }

                    return Results.Ok(new
                    {
                        ok = true,
                        plugin_id = plugin.Id,
                        config = LoadTomlObject(pluginConfigPath),
                        schema = GetPluginConfigSchema(plugin.AssemblyPath),
                        routes = CreatePluginRouteResponse(routePolicy, plugin.Id)
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = "invalid_config", message = ex.Message });
                }
            }
        });



        api.MapPost("/plugins/{id}/enable", async (string id) =>
        {
            var gate = GetPluginOperationLock(id);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (FindLoadedPlugin(pluginManager, id) is not null)
                {
                    return Results.Ok(new { ok = true, message = $"插件 {id} 已启用" });
                }

                RestoreDisabledPluginFile(pluginManager, id);
                if (pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).Count == 0)
                {
                    return Results.NotFound(new { ok = false, message = $"未找到插件文件: {id}" });
                }

                await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, id).ConfigureAwait(false);
                return Results.Ok(new { ok = true, message = $"插件 {id} 已启用" });
            }
            finally
            {
                gate.Release();
            }
        });

        api.MapPost("/plugins/{id}/disable", async (string id) =>
        {
            var gate = GetPluginOperationLock(id);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var plugin = FindLoadedPlugin(pluginManager, id);
                var targetPath = plugin?.AssemblyPath
                                 ?? pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault();
                if (targetPath is null && FindDisabledPluginFile(pluginManager, id) is not null)
                {
                    return Results.Ok(new { ok = true, message = $"插件 {id} 已禁用" });
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return Results.NotFound(new { ok = false, message = $"未找到插件文件: {id}" });
                }

                if (plugin is not null)
                {
                    await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, plugin.Name).ConfigureAwait(false);
                }

                try
                {
                    DisablePluginFile(targetPath);
                }
                catch (IOException ex)
                {
                    return Results.Conflict(new { ok = false, message = $"插件已卸载，但 DLL 文件仍被占用，请稍后重试: {ex.Message}" });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Conflict(new { ok = false, message = $"插件已卸载，但 DLL 文件仍被占用，请稍后重试: {ex.Message}" });
                }

                return Results.Ok(new { ok = true, message = $"插件 {plugin?.Name ?? id} 已禁用" });
            }
            finally
            {
                gate.Release();
            }
        });

        api.MapPost("/plugins/{id}/delete", async (string id) =>
        {
            var plugin = FindLoadedPlugin(pluginManager, id);
            var targetPath = plugin?.AssemblyPath
                             ?? pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault()
                             ?? FindDisabledPluginFile(pluginManager, id);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return Results.NotFound(new { ok = false, message = $"未找到插件文件: {id}" });
            }

            if (plugin is not null)
            {
                await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, plugin.Name).ConfigureAwait(false);
            }

            DeletePluginPath(pluginManager.PluginRootPath, targetPath, plugin?.Name ?? id);
            return Results.Ok(new { ok = true, message = $"插件 {id} 已删除" });
        });

        api.MapPost("/plugins/{id}/update", async (string id, HttpContext context) =>
        {
            var plugin = FindLoadedPlugin(pluginManager, id);
            if (plugin is null)
            {
                return Results.NotFound(new { ok = false, message = $"未找到已加载插件: {id}" });
            }

            if (string.IsNullOrWhiteSpace(plugin.GithubRepo))
            {
                return Results.BadRequest(new { ok = false, message = $"插件 {plugin.Name} 未配置 GithubRepo" });
            }

            var update = await Updater.CheckGitHubPluginPackageUpdateAsync(
                plugin.GithubRepo,
                plugin.Version,
                cancellationToken: context.RequestAborted).ConfigureAwait(false);
            if (update is null)
            {
                return Results.Ok(new { ok = true, message = $"插件 {plugin.Name} 已是最新版本" });
            }

            if (string.IsNullOrWhiteSpace(update.AssetDownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName))
            {
                return Results.BadRequest(new { ok = false, message = $"插件 {plugin.Name} 有新版本，但 release 中没有可用的 zip 或 dll 插件包" });
            }

            await Updater.ApplyPluginUpdateAsync(
                plugin.Name,
                update.AssetDownloadUrl,
                plugin.AssemblyPath,
                () => pluginManager.ScheduleUnloadPluginByName(eventDispatcher, plugin.Name),
                () => pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, plugin.Name),
                context.RequestAborted).ConfigureAwait(false);

            return Results.Ok(new
            {
                ok = true,
                message = $"插件 {plugin.Name} 已更新到 {update.LatestVersion}",
                current_version = update.CurrentVersion,
                latest_version = update.LatestVersion,
                release_url = update.ReleaseUrl
            });
        });
    }

}
