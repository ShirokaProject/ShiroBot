using System.Text.Json.Nodes;
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
            HandleAsync = HandleAsync
        });
        return Task.CompletedTask;
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
