using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Verification;

/// <summary>
///     自定义网页的安全相关检查：需要确认的操作按声明校验并由宿主保存副本；
///     JSON 读写不能拉住可回收的插件程序集。
/// </summary>
internal static class PluginWebSafetyVerification
{
    private const string PluginId = "web-json-probe";

    public static async Task RunAsync()
    {
        VerifyConfirmations();
        await VerifyJsonUnloadAsync();
    }

    /// <summary>需要确认的操作：格式不对时注册失败；注册后插件再改原字典不影响 Dashboard 收到的内容。</summary>
    private static void VerifyConfirmations()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.WebConfirm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logs = new HostLogHub();
            var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
            var context = new PluginContext(bot, "web-confirm", root, logs, new PluginServiceRegistry());
            var assembly = typeof(PluginWebSafetyVerification).Assembly;
            var declared = new Dictionary<string, string> { ["delete"] = "删除这个条目？", ["DELETE /memes"] = "删除表情？" };
            using (context.Dashboard.AddWebPage("ok", "OK",
                       new DashboardWebPage(assembly, "test-web/") { Confirmations = declared, StrictDom = true }))
            {
                declared["delete"] = "changed";
                declared["clear"] = "清空？";
                var page = context.DashboardScope.Pages.Single();
                Check(
                    page.WebConfirmations is { Count: 2 } saved && saved["delete"] == "删除这个条目？" && page.Web!.StrictDom,
                    "Confirmations must be copied when the page registers.");
            }

            foreach (var invalid in new Dictionary<string, string>[]
                     {
                         new() { ["delete everything"] = "x" }, new() { ["delete /memes"] = "x" },
                         new() { ["DELETE memes"] = "x" },
                         new() { ["DELETE /memes?all=1"] = "x" }, new() { ["DELETE /../config"] = "x" },
                         new() { ["delete"] = "" },
                         new() { ["delete"] = new string('x', 201) }, new() { ["del\nete"] = "x" }
                     })
                try
                {
                    context.Dashboard.AddWebPage("bad", "Bad",
                        new DashboardWebPage(assembly, "test-web/") { Confirmations = invalid }).Dispose();
                    throw new InvalidOperationException("An invalid confirmation was accepted: " +
                                                        string.Join(", ", invalid.Keys));
                }
                catch (ArgumentException)
                {
                }

            Console.WriteLine("Web page confirmations are validated and copied at registration.");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task VerifyJsonUnloadAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.WebJson", Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginRoot);
        // 只复制文件，不在测试进程里引用探针插件的类型，避免它进入默认 ALC。
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ShiroBot.WebJsonPluginProbe.dll"),
            Path.Combine(pluginRoot, "ShiroBot.WebJsonPluginProbe.dll"));

        var logs = new HostLogHub();
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var resolver = new SharedAssemblyResolver();
        var manager = new PluginManager(bot, resolver, new ModelPackageRegistry(resolver), runtime, logs)
            { PluginRootPath = pluginRoot };
        var dispatcher = new HostEventDispatcher(new Lock(), bot.ReplySubscriptions, runtime, logs);
        try
        {
            await manager.ScheduleLoadPluginByName(dispatcher, new PluginRouteConfig(), PluginId);
            Check(manager.GetLoadedPluginNames().Contains(PluginId), "The web JSON probe did not load.");

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using (var app = builder.Build())
            {
                typeof(HostHttpServer).GetMethod("MapPluginPageEndpoints",
                        BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [app.MapGroup("/api/v1"), new ApiHostConfig { Token = "web-json-key" }, manager]);
                await app.StartAsync();
                // Assign settings after taking ownership so a setter failure also disposes the client.
                // ReSharper disable once UseObjectOrCollectionInitializer
                using var http = new HttpClient();
                http.BaseAddress = new Uri(app.Urls.Single());
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "web-json-key");

                async Task<JsonNode> PostAsync(string path)
                {
                    // The callback completes before its enclosing resource is disposed; timeout cleanup is deferred until it completes.
                    // ReSharper disable once AccessToDisposedClosure
                    using var response = await http.PostAsync(
                        $"/api/v1/plugins/{PluginId}/pages/web/web/request?path={Uri.EscapeDataString(path)}",
                        new StringContent("{\"name\":\"cat\"}", Encoding.UTF8, "application/json"));
                    var body = await response.Content.ReadAsStringAsync();
                    Check(response.IsSuccessStatusCode, $"{path} failed: {(int)response.StatusCode} {body}");
                    return JsonNode.Parse(body)!;
                }

                // 页面列表把确认项和严格 DOM 交给 Dashboard；组件页面没有这些字段。
                // 这一步还让宿主先用上自己的 JSON 选项：设置相同的选项会共用 System.Text.Json 的全局类型缓存，
                // 插件请求若也用这份缓存就永远无法卸载，所以必须在插件请求之前发生。
                using (var list = await http.GetAsync($"/api/v1/plugins/{PluginId}/pages"))
                {
                    var pages = JsonNode.Parse(await list.Content.ReadAsStringAsync())!["pages"]!.AsArray();
                    var web = pages.Single(page => (string?)page!["id"] == "web")!;
                    var component = pages.Single(page => (string?)page!["id"] == "component")!;
                    Check(list.IsSuccessStatusCode && (string?)web["kind"] == "web" &&
                          (bool?)web["strict_dom"] == true &&
                          (string?)web["confirmations"]?["clear"] == "清空？" &&
                          (string?)web["confirmations"]?["DELETE /memes"] == "删除表情？" &&
                          component["confirmations"] is null && component["strict_dom"] is null,
                        "The page list must carry web page confirmations and strict DOM: " + pages.ToJsonString());
                }

                var reflection = await PostAsync("/reflection");
                Check((string?)reflection["name"] == "CAT" && (int?)reflection["mode"] == 1,
                    "Reflection JSON did not round-trip.");
                var generated = await PostAsync("/plugin-json");
                Check((string?)generated["name"] == "CAT" && (int?)generated["mode"] == 2,
                    "Generated plugin JSON did not round-trip.");
                await app.StopAsync();
            }

            // 宿主共用的 JSON 选项会一直缓存插件类型；每次请求新建后，只剩 System.Text.Json 内部约 1 秒后过期、
            // 下次使用时清理的反射缓存。等它过期并触发一次清理，再检查程序集是否释放。
            await Task.Delay(TimeSpan.FromSeconds(2));
            _ = JsonSerializer.Serialize(new Dictionary<string, int> { ["touch"] = 1 });
            Check(await manager.ScheduleUnloadPluginByName(dispatcher, PluginId),
                "The web JSON probe refused to unload.");
            var notice = JsonSerializer.Serialize(runtime.CreateRestartNotice("plugin"));
            if (!notice.Contains("\"recommended\":false"))
            {
                foreach (var log in logs.GetHistory("system", 50)) Console.WriteLine(log.Message);
                throw new InvalidOperationException("Web page JSON kept the plugin assembly alive after unload: " +
                                                    notice);
            }

            Console.WriteLine("Web page JSON (reflection and generated) releases the plugin assembly after unload.");
        }
        finally
        {
            await manager.UnloadAllAsync(dispatcher);
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}