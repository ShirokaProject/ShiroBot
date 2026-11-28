using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
                var source = context.Request.Query.TryGetValue("source", out var sourceValues)
                    ? sourceValues.ToString()
                    : "all";
                var tail = context.Request.Query.TryGetValue("tail", out var tailValues) &&
                           int.TryParse(tailValues.ToString(), out var parsedTail)
                    ? parsedTail
                    : 100;

                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache, no-transform";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
                try
                {
                    await logHub.StreamAsync(context.Response.Body, source, tail, context.RequestAborted).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or OperationCanceledException)
                {
                    context.Abort(); // Slow/disconnected clients reconnect and receive a fresh bounded history.
                }
            });
        }
    }
}