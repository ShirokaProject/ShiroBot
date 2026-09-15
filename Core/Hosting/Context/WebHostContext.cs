using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal sealed class WebHostContext(string publicBaseUrl, bool isEnabled) : IWebHostContext
{
    private readonly ConcurrentDictionary<string, WebFileEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WebRouteEntry> _routes = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled { get; } = isEnabled;

    public string RegisterFile(
        string ownerId,
        string routePrefix,
        string filePath,
        TimeSpan? expiresAfter = null,
        string? contentType = null)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("宿主 API 服务未启用。");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var ownerRoutePrefix = GetOwnerRoutePrefix(ownerId);
        routePrefix = NormalizeRoutePrefix(routePrefix);
        var fullPath = Path.GetFullPath(filePath);
        var token = CreateUniqueToken(ownerRoutePrefix, routePrefix);
        var routePath = $"{ownerRoutePrefix}/{routePrefix}/{token}";
        var expiresAt = expiresAfter is { TotalSeconds: > 0 }
            ? DateTimeOffset.UtcNow.Add(expiresAfter.Value)
            : DateTimeOffset.MaxValue;

        _files[routePath] = new WebFileEntry(
            ownerId,
            fullPath,
            Path.GetFileName(fullPath),
            expiresAt,
            contentType ?? GuessContentType(fullPath));

        return $"{NormalizeBaseUrl(publicBaseUrl).TrimEnd('/')}/{routePath}";
    }

    public string Map(
        string ownerId,
        string method,
        string routePath,
        Func<HttpContext, Task<IResult>> handler)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("宿主 API 服务未启用。");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePath);
        ArgumentNullException.ThrowIfNull(handler);

        var normalizedMethod = NormalizeMethod(method);
        var fullRoutePath = $"{GetOwnerRoutePrefix(ownerId)}/{NormalizeRoutePrefix(routePath)}";
        _routes[CreateRouteKey(normalizedMethod, fullRoutePath)] = new WebRouteEntry(ownerId, normalizedMethod, fullRoutePath, handler);

        return $"{NormalizeBaseUrl(publicBaseUrl).TrimEnd('/')}/{fullRoutePath}";
    }

    public string MapGet(
        string ownerId,
        string routePath,
        Func<HttpContext, Task<IResult>> handler) =>
        Map(ownerId, HttpMethods.Get, routePath, handler);

    public string MapPost(
        string ownerId,
        string routePath,
        Func<HttpContext, Task<IResult>> handler) =>
        Map(ownerId, HttpMethods.Post, routePath, handler);

    public void UnregisterOwner(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return;

        foreach (var (token, entry) in _files.ToArray())
        {
            if (string.Equals(entry.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                _files.TryRemove(token, out _);
            }
        }

        foreach (var (key, entry) in _routes.ToArray())
        {
            if (string.Equals(entry.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                _routes.TryRemove(key, out _);
            }
        }
    }

    internal async Task<IResult> HandleRequest(HttpContext context)
    {
        var routePath = context.Request.Path.Value?.Trim('/');
        if (string.IsNullOrWhiteSpace(routePath))
        {
            return Results.NotFound();
        }

        if (TryGetRoute(context.Request.Method, routePath, out var route))
        {
            return await route.Handler(context).ConfigureAwait(false);
        }

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return Results.NotFound();
        }

        if (!_files.TryGetValue(routePath, out var entry))
        {
            return Results.NotFound();
        }

        if (entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _files.TryRemove(routePath, out _);
            return Results.NotFound();
        }

        if (!File.Exists(entry.FilePath))
        {
            _files.TryRemove(routePath, out _);
            return Results.NotFound();
        }

        return Results.File(entry.FilePath, entry.ContentType, enableRangeProcessing: true);
    }

    private bool TryGetRoute(string method, string routePath, out WebRouteEntry route)
    {
        var normalizedMethod = NormalizeMethod(method);
        if (_routes.TryGetValue(CreateRouteKey(normalizedMethod, routePath), out route!)) return true;
        return HttpMethods.IsHead(normalizedMethod) && _routes.TryGetValue(CreateRouteKey(HttpMethods.Get, routePath), out route!);
    }

    private string CreateUniqueToken(string ownerRoutePrefix, string routePrefix)
    {
        for (var i = 0; i < 32; i++)
        {
            var token = GenerateShortToken();
            if (!_files.ContainsKey($"{ownerRoutePrefix}/{routePrefix}/{token}")) return token;
        }

        return Guid.NewGuid().ToString("N")[..8];
    }

    private static string GenerateShortToken()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> random = stackalloc byte[4];
        RandomNumberGenerator.Fill(random);
        Span<char> result = stackalloc char[4];

        for (var i = 0; i < 4; i++)
        {
            result[i] = chars[random[i] % chars.Length];
        }

        return new string(result);
    }

    private static string NormalizeBaseUrl(string url)
    {
        url = url.Trim();
        return url.EndsWith('/') ? url : url + "/";
    }

    private static string NormalizeRoutePrefix(string routePrefix) => routePrefix.Trim().Trim('/');

    private static string NormalizeMethod(string method) => method.Trim().ToUpperInvariant();

    private static string CreateRouteKey(string method, string routePath) => $"{method}:{routePath}";

    private static string GetOwnerRoutePrefix(string ownerId) => $"plugin/{NormalizeRouteSegment(ownerId)}";

    private static string NormalizeRouteSegment(string value)
    {
        value = value.Trim().Trim('/');
        return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'));
    }

    private static string GuessContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}

internal sealed record WebFileEntry(
    string OwnerId,
    string FilePath,
    string FileName,
    DateTimeOffset ExpiresAt,
    string ContentType);

internal sealed record WebRouteEntry(
    string OwnerId,
    string Method,
    string RoutePath,
    Func<HttpContext, Task<IResult>> Handler);
