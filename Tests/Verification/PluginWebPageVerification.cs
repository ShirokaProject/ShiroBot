using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

internal static class PluginWebPageVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shiro-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logs = new HostLogHub();
            var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
            var context = new PluginContext(bot, "web-test", root, logs, new PluginServiceRegistry());
            var plugin = new WebPlugin();
            await plugin.OnLoad(context);
            var handle = new LoadedPluginHandle(plugin, context, new DllLoader<IBotPlugin>(), typeof(WebPlugin).Assembly.Location,
                new PluginProbeInfo(typeof(WebPlugin).FullName!, "web-test", "Web", "1.0.0", null, null, PluginCategory.Other, null, false, [], []), logs);
            Check(PluginDashboard.List(handle).Single().Web is not null, "Web metadata missing.");
            try { context.Dashboard.AddPage("web", "duplicate", (_, _) => Task.FromResult<UiNode>(Ui.Column())); throw new Exception("Duplicate accepted"); }
            catch (InvalidOperationException) { }
            Check((await PluginDashboard.WebDocumentAsync(handle, "web", default)).Value!.Contains("自定义编辑器"), "Embedded HTML not loaded.");
            foreach (var path in new[] { "../index.html", "/index.html", "x/../index.html", "x\\index.html", "%2e%2e/index.html", "index.html?key=x", "x//index.html", "missing" })
                Check((await PluginDashboard.WebAssetAsync(handle, "web", path, default)).Status == PluginPageStatus.NotFound, "Unsafe asset path accepted: " + path);
            Check((await PluginDashboard.WebCallAsync(handle, "other", "echo", null, default)).Status == PluginPageStatus.NotFound, "Cross-page call reached plugin.");
            var args = new JsonObject { ["text"] = "original" };
            var result = await PluginDashboard.WebCallAsync(handle, "web", "echo", args, default);
            Check(args["text"]!.GetValue<string>() == "original" && result.Value!["text"]!.GetValue<string>() == "changed", "Request JSON was not copied.");
            plugin.Result["text"] = "later";
            Check(result.Value!["text"]!.GetValue<string>() == "changed", "Response retained plugin JSON.");
            Check((await PluginDashboard.WebCallAsync(handle, "web", "large", null, default)).Status == PluginPageStatus.Failed, "Oversized JSON response accepted.");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await PluginDashboard.WebCallAsync(handle, "web", "echo", null, canceled.Token); throw new Exception("Cancellation swallowed"); }
            catch (OperationCanceledException) { }

            var manager = new PluginManager(bot, null!, null!, null!, logs);
            ((List<LoadedPluginHandle>)typeof(PluginManager).GetField("_loadedPlugins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!).Add(handle);
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app = builder.Build();
            typeof(HostHttpServer).GetMethod("MapPluginPageEndpoints", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [app.MapGroup("/api/v1"), new ApiHostConfig { Token = "web-test-key" }, manager]);
            await app.StartAsync();
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            const string page = "/api/v1/plugins/web-test/pages/web/web";
            foreach (var (method, path) in new[] { (HttpMethod.Get, page), (HttpMethod.Get, page + "/assets?path=index.html"), (HttpMethod.Post, page + "/call") })
            {
                using var denied = await http.SendAsync(new HttpRequestMessage(method, path));
                Check(denied.StatusCode == HttpStatusCode.Unauthorized, "Web endpoint bypassed Bearer auth.");
            }
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "web-test-key");
            using var asset = await http.GetAsync(page + "/assets?path=index.html");
            Check(asset.IsSuccessStatusCode && asset.Headers.GetValues("Content-Security-Policy").Single().Contains("sandbox"), "Asset response is not isolated.");
            using var valid = await http.PostAsync(page + "/call", new StringContent("{\"action\":\"echo\",\"arguments\":{\"text\":\"test\"}}", Encoding.UTF8, "application/json"));
            Check(valid.IsSuccessStatusCode, "Authenticated bridge call failed.");
            using var oversized = await http.PostAsync(page + "/call", new StringContent(new string('x', 1_000_001)));
            Check(oversized.StatusCode == HttpStatusCode.BadRequest, "Oversized request accepted.");
            using var saved = (await PluginDashboard.WebAssetAsync(handle, "web", "index.html", default)).Value!.Content;
            var waiting = PluginDashboard.WebCallAsync(handle, "web", "wait", null, default);
            await plugin.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var unload = handle.UnloadAsync();
            try { await waiting; throw new Exception("Unload did not cancel call"); } catch (OperationCanceledException) { }
            await unload;
            Check(PluginDashboard.List(handle).Count == 0 && saved.CanRead, "Unload did not remove pages or destroyed host-owned file.");
            await app.StopAsync();
            Console.WriteLine("Web pages: embedded resources, isolation, Bearer authentication, bounds, JSON copying and unload cancellation passed.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class WebPlugin : PluginBase
    {
        public JsonObject Result { get; } = new() { ["text"] = "changed" };
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task LoadAsync()
        {
            Context.Dashboard.AddWebPage("web", "Web", new DashboardWebPage(typeof(WebPlugin).Assembly, "test-web/") { HandleAsync = Handle });
            using var registration = Context.Dashboard.AddWebPage("temporary", "Temporary", new DashboardWebPage(typeof(WebPlugin).Assembly, "test-web/"));
            return Task.CompletedTask;
        }
        private async Task<JsonNode?> Handle(string action, JsonNode? args, CancellationToken ct)
        {
            if (action == "wait") { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            if (action == "large") return JsonValue.Create(new string('x', 1_000_001));
            if (args is JsonObject obj) obj["text"] = "mutated";
            return Result;
        }
    }
}
