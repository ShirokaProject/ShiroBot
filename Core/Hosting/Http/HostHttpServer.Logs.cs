using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    internal sealed partial class DiagnosticsEndpoints
    {
        private void MapLogs(RouteGroupBuilder api)
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
}