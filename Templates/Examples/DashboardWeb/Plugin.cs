using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

[BotPlugin("dashboard-web-demo", Name = "网页页面示例", IsPluginSingleFile = true)]
public sealed class WebDemo : PluginBase
{
    protected override Task LoadAsync()
    {
        Context.Dashboard.AddWebPage("editor", "自定义编辑器", new DashboardWebPage(typeof(WebDemo).Assembly, "dashboard/")
        {
            Icon = "edit",
            HandleAsync = HandleAsync,
            RequestAsync = RequestAsync,
            MaxRequestBytes = 32 * 1024 * 1024,
            MaxResponseBytes = 32 * 1024 * 1024
        });
        return Task.CompletedTask;
    }

    private static async Task<IResult> RequestAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.Method == "POST" && request.Path == "/files/echo")
        {
            var form = await request.ReadFormAsync(ct);
            if (form.Files.GetFile("file") is not { } file)
                return DashboardWebJson.Json(new JsonObject { ["error"] = "请选择文件。" }, StatusCodes.Status400BadRequest);
            // ASP.NET buffers multipart uploads to disk above its memory threshold.
            // The result stream is sent and disposed before the upload buffer is cleaned up.
            return Results.Stream(file.OpenReadStream(), file.ContentType, fileDownloadName: Path.GetFileName(file.FileName));
        }
        if (request.Method is "GET" or "HEAD" && request.Path == "/sample.bin")
            return Results.File(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, "application/octet-stream", enableRangeProcessing: true);
        // JSON 用 DashboardWebJson：JsonNode 或标了 [PluginJson] 的类型，不经过反射序列化，插件可以正常热重载。
        return DashboardWebJson.Json(new JsonObject { ["error"] = "路由不存在。" }, StatusCodes.Status404NotFound);
    }

    private static Task<JsonNode?> HandleAsync(string action, JsonNode? args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (action != "preview" || args is not JsonObject input || input["text"] is not JsonValue text ||
            !text.TryGetValue<string>(out var value) || value.Length > 2000)
            throw new ArgumentException("需要 preview 操作和不超过 2000 字符的 text。");
        return Task.FromResult<JsonNode?>(new JsonObject { ["text"] = value, ["length"] = value.Length });
    }
}
