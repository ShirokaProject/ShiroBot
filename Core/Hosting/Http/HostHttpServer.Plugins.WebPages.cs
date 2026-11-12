using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Configuration;
using ShiroBot.Plugins;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static void MapDashboardWebPageEndpoints(RouteGroupBuilder api, ApiHostConfig config,
        string prefix, Func<string, IDashboardOwner?> find)
    {
        api.MapMethods($"{prefix}/{{id}}/pages/{{pageId}}/web/request",
            ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
            async (string id, string pageId, HttpContext context) =>
            {
                if (Unauthorized(context, config) is { } denied) return denied;
                if (find(id) is not { } plugin) return Results.NotFound();
                PluginPageOutcome<bool> outcome;
                try
                {
                    outcome = await PluginDashboard
                        .WebRequestAsync(plugin, pageId, context.Request.Query["path"].ToString(), context)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
                    {
                        return Results.Conflict(new { error = "request_canceled", message = "页面请求已取消。" });
                    }

                    {
                        context.Abort();
                        return Results.Empty;
                    }
                }

                if (outcome.Status == PluginPageStatus.Done) return Results.Empty;
                if (context.Response.HasStarted)
                {
                    context.Abort();
                    return Results.Empty;
                }

                context.Response.Headers.Clear();
                return PageFailure(outcome.Status, outcome.Message, outcome.Errors);
            });

        api.MapGet($"{prefix}/{{id}}/pages/{{pageId}}/web", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (find(id) is not { } plugin) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            var outcome = await PluginDashboard.WebDocumentAsync(plugin, pageId, context.RequestAborted)
                .ConfigureAwait(false);
            return outcome.Status == PluginPageStatus.Done
                ? Results.Ok(new { html = outcome.Value })
                : PageFailure(outcome.Status, outcome.Message, null);
        });

        api.MapGet($"{prefix}/{{id}}/pages/{{pageId}}/web/assets", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (find(id) is not { } plugin) return Results.NotFound();
            var outcome = await PluginDashboard
                .WebAssetAsync(plugin, pageId, context.Request.Query["path"].ToString(), context.RequestAborted)
                .ConfigureAwait(false);
            if (outcome.Status != PluginPageStatus.Done) return PageFailure(outcome.Status, outcome.Message, null);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var file = outcome.Value!;
            context.Response.RegisterForDispose(file.Content);
            return Results.File(file.Content, file.ContentType);
        });

        api.MapPost($"{prefix}/{{id}}/pages/{{pageId}}/web/call", async (string id, string pageId, HttpContext context) =>
        {
            if (Unauthorized(context, config) is { } denied) return denied;
            if (find(id) is not { } plugin) return Results.NotFound();
            if (await ReadPageRequestAsync(context).ConfigureAwait(false) is not { } body ||
                PageString(body["action"]) is not { } action)
                return Results.BadRequest(new { error = "invalid_request", message = "请求内容无效。" });
            context.Response.Headers.CacheControl = "no-store";
            var outcome = await PluginDashboard
                .WebCallAsync(plugin, pageId, action, body["arguments"], context.RequestAborted).ConfigureAwait(false);
            return outcome.Status == PluginPageStatus.Done
                ? Results.Text(new JsonObject { ["value"] = outcome.Value }.ToJsonString(),
                    "application/json; charset=utf-8")
                : PageFailure(outcome.Status, outcome.Message, null);
        });
    }
}