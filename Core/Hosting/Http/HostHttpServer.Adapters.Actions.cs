using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    internal sealed partial class ComponentEndpoints
    {
        private void MapAdapterActions(RouteGroupBuilder api)
        {
            api.MapGet("/adapters/{id}/actions", async (string id, HttpContext context) =>
            {
                if (!IsBearerAuthorized(context, options.Value)) return Results.Unauthorized();
                try
                {
                    var actions = await adapterManager.GetActionsAsync(id, context.RequestAborted);
                    return Results.Ok(new { actions = actions.Select(x => new
                    {
                        id = x.Id, label = x.Label, description = x.Description, tone = NormalizePluginActionTone(x.Tone),
                        requires_confirmation = x.RequiresConfirmation, confirmation_text = x.ConfirmationText
                    }) });
                }
                catch (KeyNotFoundException ex) { return Results.NotFound(new { error = "adapter_not_found", message = ex.Message }); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return Results.StatusCode(499); }
                catch (Exception ex) { return Results.Json(new { error = "action_provider_failed", message = ex.Message }, statusCode: 500); }
            });
            api.MapPost("/adapters/{id}/actions/{actionId}", async (string id, string actionId, HttpContext context) =>
            {
                if (!IsBearerAuthorized(context, options.Value)) return Results.Unauthorized();
                try
                {
                    var result = await adapterManager.ExecuteActionAsync(id, actionId, context.RequestAborted);
                    return Results.Ok(new { ok = result.Ok, message = result.Message, refresh = result.Refresh, title = result.Title, details = result.Details });
                }
                catch (KeyNotFoundException ex) { return Results.NotFound(new { error = "action_not_found", message = ex.Message }); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return Results.StatusCode(499); }
                catch (Exception ex) { return Results.Json(new { ok = false, message = ex.Message, refresh = false }, statusCode: 500); }
            });
        }
    }
}
