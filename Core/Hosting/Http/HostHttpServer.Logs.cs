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
    private static void MapLogEndpoints(RouteGroupBuilder api, HostLogHub logHub)
    {
        api.MapGet("/logs/sources", () => Results.Ok(logHub.GetSources()));
        api.MapGet("/logs/stream", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Expected WebSocket request.").ConfigureAwait(false);
                return;
            }

            var source = context.Request.Query.TryGetValue("source", out var sourceValues)
                ? sourceValues.ToString()
                : "all";
            var tail = context.Request.Query.TryGetValue("tail", out var tailValues) &&
                       int.TryParse(tailValues.ToString(), out var parsedTail)
                ? parsedTail
                : 100;

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            await logHub.StreamAsync(webSocket, source, tail, context.RequestAborted).ConfigureAwait(false);
        });
    }

}
