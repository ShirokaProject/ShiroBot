using Microsoft.AspNetCore.Http;

namespace ShiroBot.SDK.Plugin;

public interface IWebHostContext
{
    bool IsEnabled { get; }

    string RegisterFile(
        string ownerId,
        string routePrefix,
        string filePath,
        TimeSpan? expiresAfter = null,
        string? contentType = null);

    string Map(
        string ownerId,
        string method,
        string routePath,
        Func<HttpContext, Task<IResult>> handler);

    string MapGet(
        string ownerId,
        string routePath,
        Func<HttpContext, Task<IResult>> handler);

    string MapPost(
        string ownerId,
        string routePath,
        Func<HttpContext, Task<IResult>> handler);

    void UnregisterOwner(string ownerId);
}
