using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Dashboard;

/// <summary>
/// 自定义网页 <see cref="DashboardWebPage.RequestAsync"/> 的 JSON 读写。
/// 使用 <see cref="PluginJsonAttribute"/> 在插件程序集里生成的读写代码，不经过反射序列化，
/// 不会在宿主里留下插件类型的缓存，插件可以正常卸载和热重载；不想定义类型时用 <see cref="JsonNode"/>。
/// </summary>
public static class DashboardWebJson
{
    /// <summary>把请求体读成标了 [PluginJson] 的类型；空请求体和 JSON null 得到 <c>default</c>。</summary>
    public static async Task<T?> ReadPluginJsonAsync<T>(this HttpRequest request, CancellationToken cancellationToken = default)
    {
        var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
        return PluginJson.Deserialize<T>(body.Span);
    }

    /// <summary>把请求体读成 <see cref="JsonNode"/>；空请求体得到 null。</summary>
    public static async Task<JsonNode?> ReadJsonNodeAsync(this HttpRequest request, CancellationToken cancellationToken = default)
    {
        var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
        return body.IsEmpty ? null : JsonNode.Parse(body.Span);
    }

    /// <summary>返回标了 [PluginJson] 的类型的 JSON 响应。</summary>
    public static IResult Json<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        new JsonBytesResult(PluginJson.Serialize(value), statusCode);

    /// <summary>返回 <see cref="JsonNode"/> 的 JSON 响应。</summary>
    public static IResult Json(JsonNode? value, int statusCode = StatusCodes.Status200OK) =>
        new JsonBytesResult(value is null ? [] : Encoding.UTF8.GetBytes(value.ToJsonString()), statusCode);

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 宿主已经按页面的 MaxRequestBytes 限制了请求体大小。
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private sealed class JsonBytesResult(byte[] json, int statusCode) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            // 空字节表示 JSON null。
            var body = json.Length == 0 ? "null"u8.ToArray() : json;
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            httpContext.Response.ContentLength = body.Length;
            await httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
