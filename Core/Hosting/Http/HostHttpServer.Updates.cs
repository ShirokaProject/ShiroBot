using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Update;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static void MapHostUpdateEndpoints(RouteGroupBuilder api, ConfigManager configManager, HostPowerControl powerControl)
    {
        async Task<HostUpdateCheck> CheckAsync(CancellationToken token)
        {
            var config = await configManager.LoadCoreConfig().ConfigureAwait(false);
            return await HostSelfUpdater.CheckAsync(config.HostUpdateRepository, token).ConfigureAwait(false);
        }

        api.MapGet("/system/update", async (HttpContext context) =>
        {
            try
            {
                var check = await CheckAsync(context.RequestAborted).ConfigureAwait(false);
                return Results.Ok(new
                {
                    current_version = check.CurrentVersion,
                    latest_version = check.LatestVersion,
                    update_available = check.UpdateAvailable,
                    asset_name = check.AssetName,
                    release_url = check.ReleaseUrl,
                    release_notes = check.ReleaseNotes,
                    can_apply = check.CanApply,
                    reason = check.Reason
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { ok = false, message = "检查宿主更新失败: " + ex.Message });
            }
        });

        api.MapPost("/system/update", async (HttpContext context) =>
        {
            try
            {
                // Resolve the release on the server again; the client never supplies an executable URL.
                var check = await CheckAsync(context.RequestAborted).ConfigureAwait(false);
                if (!check.UpdateAvailable)
                    return Results.Ok(new { ok = true, restarting = false, message = check.Reason ?? "宿主已是最新版本。" });
                if (!check.CanApply)
                    return Results.BadRequest(new { ok = false, message = check.Reason });
                var result = await HostSelfUpdater.ApplyAsync(check.AssetDownloadUrl!, powerControl, context.RequestAborted).ConfigureAwait(false);
                return result.Ok
                    ? Results.Ok(new { ok = true, restarting = true, message = result.Message })
                    : Results.Conflict(new { ok = false, message = result.Message });
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { ok = false, message = "宿主更新失败: " + ex.Message });
            }
        });
    }
}
