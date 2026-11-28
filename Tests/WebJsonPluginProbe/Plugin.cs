using Microsoft.AspNetCore.Http;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.WebJsonPluginProbe;

/// <summary>
/// 验证自定义网页的 JSON 读写不会拉住可回收的插件程序集：/reflection 用 ASP.NET 的反射序列化，
/// /plugin-json 用 SDK 生成的读写代码。由 PluginWebPageVerification 复制到临时插件目录加载，测试代码不引用这里的类型。
/// </summary>
[BotPlugin("web-json-probe", Name = "Web JSON probe", IsPluginSingleFile = true)]
public sealed class WebJsonProbe : PluginBase
{
    protected override Task LoadAsync()
    {
        Context.Dashboard.AddWebPage("web", "JSON", new DashboardWebPage(typeof(WebJsonProbe).Assembly, "web/")
        {
            RequestAsync = RequestAsync,
            Confirmations = new Dictionary<string, string> { ["clear"] = "清空？", ["DELETE /memes"] = "删除表情？" },
            StrictDom = true
        });
        // 组件页面作对照：页面列表里不应带网页专用的字段。
        Context.Dashboard.AddPage("component", "组件", (_, _) => Task.FromResult(Ui.Text("ok")));
        return Task.CompletedTask;
    }

    private static async Task<IResult> RequestAsync(HttpRequest request, CancellationToken ct)
    {
        switch (request.Path.Value)
        {
            case "/reflection":
                var input = await request.ReadFromJsonAsync<ProbeInput>(ct);
                return Results.Json(new ProbeResult(input!.Name.ToUpperInvariant(), 1));
            case "/plugin-json":
                var typed = await request.ReadPluginJsonAsync<ProbeInput>(ct);
                return DashboardWebJson.Json(new ProbeResult(typed!.Name.ToUpperInvariant(), 2));
            default:
                return Results.NotFound();
        }
    }
}

[PluginJson]
public sealed record ProbeInput(string Name);

[PluginJson]
public sealed record ProbeResult(string Name, int Mode);
