using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.StaticFiles;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Plugins;

internal static partial class PluginDashboard
{
    internal const int MaxWebAssetBytes = 20_000_000;
    internal const int MaxWebJsonBytes = 1_000_000;

    internal static bool IsWebAssetPath(string path)
    {
        return path.Length is > 0 and <= 512 &&
               !path.Any(c => char.IsControl(c) || c is '\\' or ':' or '%' or '?' or '#') &&
               path.Split('/').All(part => part.Length > 0 && part is not "." and not "..");
    }

    public static Task<PluginPageOutcome<string>> WebDocumentAsync(IDashboardOwner plugin, string pageId,
        CancellationToken ct)
    {
        return RunAsync(plugin, pageId, "读取网页", async page =>
        {
            if (page.Web is not { } web) return NotFound<string>("这个页面不是自定义网页。");
            var bytes = await ReadWebAssetAsync(web, web.EntryPoint, 2_000_000, ct).ConfigureAwait(false);
            return bytes is null ? NotFound<string>("网页入口不存在。") : Done(new UTF8Encoding(false, true).GetString(bytes));
        });
    }

    public static Task<PluginPageOutcome<PluginPageFile>> WebAssetAsync(IDashboardOwner plugin, string pageId,
        string path, CancellationToken ct)
    {
        return RunAsync(plugin, pageId, "读取网页资源", async page =>
        {
            if (page.Web is not { } web || !IsWebAssetPath(path)) return NotFound<PluginPageFile>("资源不存在。");
            var bytes = await ReadWebAssetAsync(web, path, MaxWebAssetBytes, ct).ConfigureAwait(false);
            if (bytes is null) return NotFound<PluginPageFile>("资源不存在。");
            new FileExtensionContentTypeProvider().TryGetContentType(path, out var mime);
            return Done(
                new PluginPageFile(new MemoryStream(bytes, false), mime ?? "application/octet-stream"));
        });
    }

    public static Task<PluginPageOutcome<JsonNode?>> WebCallAsync(IDashboardOwner plugin, string pageId,
        string action,
        JsonNode? arguments, CancellationToken ct)
    {
        return RunAsync(plugin, pageId, "执行网页操作", async page =>
        {
            if (page.Web?.HandleAsync is not { } handler || action.Length is 0 or > 100 || action.Any(char.IsControl))
                return NotFound<JsonNode?>("未找到网页处理函数或操作名无效。");
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct, plugin.Dashboard!.Stopping);
            stopping.Token.ThrowIfCancellationRequested();
            var result = await handler(action, arguments?.DeepClone(), stopping.Token).ConfigureAwait(false);
            var json = result?.ToJsonString();
            if (json is not null && Encoding.UTF8.GetByteCount(json) > MaxWebJsonBytes)
                throw new InvalidOperationException("网页操作结果超过 1 MB，请通过文件接口传输大数据。");
            return Done(json is null ? null : JsonNode.Parse(json));
        });
    }

    private static async Task<byte[]?> ReadWebAssetAsync(DashboardWebPage web, string path, int limit,
        CancellationToken ct)
    {
        if (!IsWebAssetPath(path)) return null;
        await using var stream = web.Assembly.GetManifestResourceStream(web.ResourcePrefix + path);
        if (stream is null) return null;
        if (stream.Length > limit) throw new InvalidOperationException("网页资源超过大小限制。");
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidOperationException("网页资源超过大小限制。");
            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }
}