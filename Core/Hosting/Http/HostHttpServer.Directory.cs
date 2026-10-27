using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Hosting.Context;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    /// <summary>
    /// GET /directory/users and /directory/groups: what the running adapter instances can see, for the Dashboard's
    /// pickers on fields typed "users" (instanceId:userId) and "groups" (group IDs). ?refresh=1 skips the cache.
    /// </summary>
    private static void MapDirectoryEndpoints(RouteGroupBuilder api, ContactDirectory directory)
    {
        api.MapGet("/directory/{kind}", async (string kind, HttpContext context) =>
        {
            DirectoryKind? parsed = kind.ToLowerInvariant() switch
            {
                "users" => DirectoryKind.Users,
                "groups" => DirectoryKind.Groups,
                _ => null
            };
            if (parsed is not { } directoryKind)
                return Results.NotFound(new { error = "directory_not_found", message = "目录只支持 users 或 groups。" });

            var refresh = context.Request.Query.TryGetValue("refresh", out var value) && value == "1";
            var listing = await directory.GetAsync(directoryKind, refresh, context.RequestAborted).ConfigureAwait(false);
            return Results.Ok(new
            {
                entries = listing.Entries.Select(entry => new
                {
                    instance = entry.Instance,
                    instance_name = entry.InstanceName,
                    id = entry.Id,
                    name = entry.Name
                }),
                unavailable = listing.Unavailable.Select(gap => new
                {
                    instance = gap.Instance,
                    instance_name = gap.InstanceName,
                    reason = gap.Reason
                })
            });
        });
    }
}
