using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Configuration;
using ShiroBot.Plugins;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static void MapPluginWebPageEndpoints(RouteGroupBuilder api, ApiHostConfig config, PluginManager pluginManager)
    {
        api.MapGet("/plugins/{id}/pages/{pageId}/web", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            var outcome = await PluginDashboard.WebDocumentAsync(plugin, pageId, context.RequestAborted).ConfigureAwait(false);
            return outcome.Status == PluginPageStatus.Done ? Results.Ok(new { html = outcome.Value })
                : PageFailure(outcome.Status, outcome.Message, null);
        });

        api.MapGet("/plugins/{id}/pages/{pageId}/web/assets", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin) return Results.NotFound();
            var outcome = await PluginDashboard.WebAssetAsync(plugin, pageId, context.Request.Query["path"].ToString(), context.RequestAborted).ConfigureAwait(false);
            if (outcome.Status != PluginPageStatus.Done) return PageFailure(outcome.Status, outcome.Message, null);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var file = outcome.Value!;
            context.Response.RegisterForDispose(file.Content);
            return Results.File(file.Content, file.ContentType);
        });

        api.MapPost("/plugins/{id}/pages/{pageId}/web/call", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (FindLoadedPlugin(pluginManager, id) is not { } plugin) return Results.NotFound();
            if (await ReadPageRequestAsync(context).ConfigureAwait(false) is not { } body || PageString(body["action"]) is not { } action)
                return Results.BadRequest(new { error = "invalid_request", message = "请求内容无效。" });
            context.Response.Headers.CacheControl = "no-store";
            var outcome = await PluginDashboard.WebCallAsync(plugin, pageId, action, body["arguments"], context.RequestAborted).ConfigureAwait(false);
            return outcome.Status == PluginPageStatus.Done
                ? Results.Text(new JsonObject { ["value"] = outcome.Value }.ToJsonString(), "application/json; charset=utf-8")
                : PageFailure(outcome.Status, outcome.Message, null);
        });
    }
}
