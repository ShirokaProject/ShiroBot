using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Configuration;
using ShiroBot.Plugins;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Hosting.Http;

/// <summary>
/// 插件页面（<see cref="IPluginDashboardProvider"/>）的 API。全部需要 API 密钥，宿主在进程内调用插件，
/// 插件不需要开放网页路由。
/// </summary>
internal sealed partial class HostHttpServer
{
    private static void MapPluginPageEndpoints(RouteGroupBuilder api, ApiHostConfig config, PluginManager pluginManager)
    {
        api.MapGet("/plugins/{id}/pages", async (string id, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            try
            {
                var pages = await PluginDashboard.ListAsync(plugin).ConfigureAwait(false);
                if (pages is null) return Results.Conflict(new { error = "plugin_unloading", message = $"插件 {id} 正在卸载。" });
                return Results.Ok(new
                {
                    pages = pages.Select(page => new
                    {
                        id = page.Id,
                        title = page.Title,
                        kind = page.Kind,
                        icon = page.Icon,
                        description = page.Description,
                        page_size = page.PageSize,
                        upload = page.Upload is not { MaxBytes: > 0 } ? null : new
                        {
                            label = page.Upload.Label,
                            content_types = page.Upload.ContentTypes ?? [],
                            max_bytes = Math.Min(page.Upload.MaxBytes, PluginDashboard.MaxUploadBytes),
                            description = page.Upload.Description
                        },
                        columns = (page.Columns ?? []).Select(column => new { key = column.Key, label = column.Label }),
                        filters = (page.Filters ?? []).Select(filter => new
                        {
                            key = filter.Key,
                            label = filter.Label,
                            type = filter.Type == "select" ? "select" : "text",
                            options = (filter.Options ?? []).Select(option => new { value = option.Value, label = option.Label })
                        }),
                        actions = (page.Actions ?? []).Select(action => new
                        {
                            id = action.Id,
                            label = action.Label,
                            confirm = action.Confirm,
                            tone = action.Tone == "danger" ? "danger" : "default"
                        })
                    })
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Results.Json(new { error = "pages_failed", message = $"插件页面读取失败: {ex.Message}" },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        api.MapGet("/plugins/{id}/pages/{pageId}/data", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });

            var query = context.Request.Query;
            var page = int.TryParse(query["page"], out var parsedPage) ? parsedPage : 1;
            var pageSize = int.TryParse(query["page_size"], out var parsedSize) ? parsedSize : 0;
            var filters = query.Where(pair => pair.Key is not ("page" or "page_size"))
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
            var outcome = await PluginDashboard.QueryAsync(plugin, pageId, page, pageSize, filters, context.RequestAborted).ConfigureAwait(false);
            if (outcome.Status != PluginPageStatus.Done) return PageFailure(outcome.Status, outcome.Message);

            var data = outcome.Value!;
            return Results.Ok(new
            {
                items = data.Items.Select(item => new
                {
                    id = item.Id,
                    title = item.Title,
                    description = item.Description,
                    image = item.ImageFileId,
                    tags = item.Tags ?? [],
                    meta = item.Meta,
                    values = item.Values ?? new Dictionary<string, string>(),
                    actions = item.Actions ?? []
                }),
                total = data.Total,
                has_more = data.HasMore,
                metrics = (data.Metrics ?? []).Select(metric => new { label = metric.Label, value = metric.Value, hint = metric.Hint })
            });
        });

        api.MapPost("/plugins/{id}/pages/{pageId}/actions/{actionId}", async (string id, string pageId, string actionId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            var itemId = context.Request.Query["item"].ToString();
            if (string.IsNullOrWhiteSpace(itemId))
                return Results.BadRequest(new { error = "item_required", message = "缺少条目 ID。" });

            var outcome = await PluginDashboard.RunActionAsync(plugin, pageId, actionId, itemId, context.RequestAborted).ConfigureAwait(false);
            if (outcome.Status != PluginPageStatus.Done) return PageFailure(outcome.Status, outcome.Message);
            var result = outcome.Value!;
            return Results.Ok(new { ok = result.Ok, message = result.Message, refresh = result.Refresh, title = result.Title, details = result.Details });
        });

        api.MapPost("/plugins/{id}/pages/{pageId}/upload", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            var outcome = await PluginDashboard.UploadAsync(plugin, pageId, context.Request.Query["name"].ToString(),
                context.Request.ContentType ?? "application/octet-stream", context.Request.Body, context.Request.ContentLength,
                context.RequestAborted).ConfigureAwait(false);
            if (outcome.Status != PluginPageStatus.Done) return PageFailure(outcome.Status, outcome.Message);
            var result = outcome.Value!;
            return Results.Ok(new { ok = result.Ok, message = result.Message, refresh = result.Refresh, title = result.Title, details = result.Details });
        });

        api.MapGet("/plugins/{id}/pages/{pageId}/files", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            var fileId = context.Request.Query["id"].ToString();
            if (string.IsNullOrWhiteSpace(fileId))
                return Results.BadRequest(new { error = "file_required", message = "缺少文件 ID。" });

            var outcome = await PluginDashboard.OpenFileAsync(plugin, pageId, fileId, context.RequestAborted).ConfigureAwait(false);
            if (outcome.Status != PluginPageStatus.Done) return PageFailure(outcome.Status, outcome.Message);
            var file = outcome.Value!;
            // 文件随条目变化很少；短时间缓存，切换页面时不必重新读取。
            context.Response.Headers.CacheControl = "private, max-age=300";
            context.Response.RegisterForDispose(file.Content);
            return Results.File(file.Content, file.ContentType, enableRangeProcessing: true);
        });
    }

    private static IResult? Unauthorized(HttpContext context, ApiHostConfig config) =>
        IsBearerAuthorized(context, config)
            ? null
            : Results.Json(new ApiError("unauthorized", "A Bearer API key is required."), statusCode: StatusCodes.Status401Unauthorized);

    private static IResult PageFailure(PluginPageStatus status, string message) => status switch
    {
        PluginPageStatus.InvalidUpload => Results.BadRequest(new { error = "invalid_upload", message }),
        PluginPageStatus.UploadTooLarge => Results.Json(new { error = "upload_too_large", message }, statusCode: StatusCodes.Status413PayloadTooLarge),
        PluginPageStatus.NotFound => Results.NotFound(new { error = "page_not_found", message }),
        PluginPageStatus.Unloading => Results.Conflict(new { error = "plugin_unloading", message }),
        _ => Results.Json(new { error = "page_failed", message }, statusCode: StatusCodes.Status500InternalServerError)
    };
}
