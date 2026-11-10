using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Configuration;
using ShiroBot.Plugins;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Hosting.Http;

/// <summary>
///     插件页面（<see cref="IPluginDashboard" />）的 API。全部需要 API 密钥，宿主在进程内渲染页面、调用处理函数，
///     插件不需要开放网页路由。
/// </summary>
internal sealed partial class HostHttpServer
{
    private const int MaxPageRequestBytes = 1_000_000;

    private static void MapPluginPageEndpoints(RouteGroupBuilder api, ApiHostConfig config, PluginManager pluginManager)
    {
        api.MapGet("/plugins/{id}/pages", (string id, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            return Results.Ok(new
            {
                pages = PluginDashboard.List(plugin).Select(page => new
                {
                    id = page.Id, title = page.Title, icon = page.Icon, description = page.Description,
                    kind = page.Web is null ? null : "web",
                    // Dashboard 在网页之外弹出确认框，并按 strict_dom 给网页加上 Trusted Types 策略。
                    confirmations = page.Web is null ? null : page.WebConfirmations,
                    strict_dom = page.Web?.StrictDom
                })
            });
        });

        MapPluginWebPageEndpoints(api, config, pluginManager);

        api.MapPost("/plugins/{id}/pages/{pageId}/render", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            if (await ReadPageRequestAsync(context).ConfigureAwait(false) is not { } body)
                return Results.BadRequest(new { error = "invalid_request", message = "请求内容无效。" });

            var outcome = await PluginDashboard
                .RenderAsync(plugin, pageId, ReadState(body["state"]), context.RequestAborted).ConfigureAwait(false);
            return outcome.Status == PluginPageStatus.Done
                ? Results.Text(
                    new JsonObject { ["view"] = outcome.Value!.View, ["state"] = StateJson(outcome.Value.State) }
                        .ToJsonString(),
                    "application/json; charset=utf-8")
                : PageFailure(outcome.Status, outcome.Message, outcome.Errors);
        });

        api.MapPost("/plugins/{id}/pages/{pageId}/events", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            if (await ReadPageRequestAsync(context).ConfigureAwait(false) is not { } body ||
                body["event"] is not JsonObject e || PageString(e["kind"]) is not { } kind ||
                PageString(e["element"]) is not { } element)
                return Results.BadRequest(new { error = "invalid_request", message = "请求内容无效。" });

            var outcome = await PluginDashboard.EventAsync(plugin, pageId, ReadState(body["state"]),
                new PluginPageEvent(kind, element, PageString(e["action"]), PageString(e["item"]),
                    e["values"] as JsonObject),
                context.RequestAborted).ConfigureAwait(false);
            return EventResponse(outcome);
        });

        api.MapPost("/plugins/{id}/pages/{pageId}/upload", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            var query = context.Request.Query;
            JsonNode? state;
            try
            {
                state = query.TryGetValue("state", out var raw) && raw.ToString().Length > 0
                    ? JsonNode.Parse(raw.ToString())
                    : null;
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_request", message = "状态无效。" });
            }

            var outcome = await PluginDashboard.UploadAsync(plugin, pageId, ReadState(state),
                query["element"].ToString(),
                query["name"].ToString(), context.Request.ContentType ?? "application/octet-stream",
                context.Request.Body,
                context.Request.ContentLength, context.RequestAborted).ConfigureAwait(false);
            return EventResponse(outcome);
        });

        api.MapGet("/plugins/{id}/pages/{pageId}/files", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin)
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到已加载插件: {id}" });
            var fileId = context.Request.Query["id"].ToString();
            if (string.IsNullOrWhiteSpace(fileId))
                return Results.BadRequest(new { error = "file_required", message = "缺少文件 ID。" });

            var outcome = await PluginDashboard.OpenFileAsync(plugin, pageId, fileId, context.RequestAborted)
                .ConfigureAwait(false);
            if (outcome.Status != PluginPageStatus.Done) return PageFailure(outcome.Status, outcome.Message, null);
            var file = outcome.Value!;
            // 文件随条目变化很少；短时间缓存，切换页面时不必重新读取。
            context.Response.Headers.CacheControl = "private, max-age=300";
            context.Response.RegisterForDispose(file.Content);
            return Results.File(file.Content, file.ContentType, enableRangeProcessing: true);
        });
    }

    private static IResult EventResponse(PluginPageOutcome<PluginPageEventResponse> outcome)
    {
        if (outcome.Status != PluginPageStatus.Done)
            return PageFailure(outcome.Status, outcome.Message, outcome.Errors);
        var (result, state, view) = outcome.Value!;
        var json = new JsonObject
        {
            ["result"] = new JsonObject
            {
                ["ok"] = result.Ok,
                ["message"] = result.Message,
                ["details_title"] = result.DetailsTitle,
                ["details"] = result.Details,
                ["open_page"] = result.OpenPageId is null
                    ? null
                    : new JsonObject
                    {
                        ["id"] = result.OpenPageId,
                        ["state"] = StateJson(result.OpenPageState ?? new Dictionary<string, string>())
                    },
                ["open_form"] = result.OpenFormId is null
                    ? null
                    : new JsonObject
                    {
                        ["id"] = result.OpenFormId, ["values"] = result.OpenFormValues?.DeepClone()
                    }
            },
            ["state"] = StateJson(state),
            ["view"] = view
        };
        return Results.Text(json.ToJsonString(), "application/json; charset=utf-8");
    }

    /// <summary>读取页面请求的 JSON，超过大小限制或格式错误时为 null。</summary>
    private static async Task<JsonObject?> ReadPageRequestAsync(HttpContext context)
    {
        if (context.Request.ContentLength > MaxPageRequestBytes) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxPageRequestBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0) return new JsonObject();
        try
        {
            return JsonNode.Parse(buffer.ToArray()) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? PageString(JsonNode? value)
    {
        return value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
    }

    private static Dictionary<string, string> ReadState(JsonNode? state)
    {
        return PluginDashboard.CleanState((state as JsonObject)?.Select(pair =>
            new KeyValuePair<string, string?>(pair.Key, pair.Value?.GetValueKind() == JsonValueKind.String
                ? pair.Value.GetValue<string>()
                : pair.Value?.ToJsonString())));
    }

    private static JsonObject StateJson(IReadOnlyDictionary<string, string> state)
    {
        return new JsonObject(state.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value)));
    }

    private static IResult? Unauthorized(HttpContext context, ApiHostConfig config)
    {
        return IsBearerAuthorized(context, config)
            ? null
            : Results.Json(new ApiError("unauthorized", "A Bearer API key is required."),
                statusCode: StatusCodes.Status401Unauthorized);
    }

    private static IResult PageFailure(PluginPageStatus status, string message,
        IReadOnlyDictionary<string, string>? errors)
    {
        return status switch
        {
            PluginPageStatus.InvalidForm =>
                Results.BadRequest(new { error = "invalid_form", message, fields = errors }),
            PluginPageStatus.InvalidUpload => Results.BadRequest(new { error = "invalid_upload", message }),
            PluginPageStatus.UploadTooLarge => Results.Json(new { error = "upload_too_large", message },
                statusCode: StatusCodes.Status413PayloadTooLarge),
            PluginPageStatus.NotFound => Results.NotFound(new { error = "page_not_found", message }),
            PluginPageStatus.Stale => Results.Conflict(new { error = "page_changed", message }),
            PluginPageStatus.Unloading => Results.Conflict(new { error = "plugin_unloading", message }),
            _ => Results.Json(new { error = "page_failed", message },
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}