using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
            await VerifyRequestsAsync(http, page, plugin, context);
            using var asset = await http.GetAsync(page + "/assets?path=index.html");
            Check(asset.IsSuccessStatusCode && asset.Headers.GetValues("Content-Security-Policy").Single().Contains("sandbox"), "Asset response is not isolated.");
            using var valid = await http.PostAsync(page + "/call", new StringContent("{\"action\":\"echo\",\"arguments\":{\"text\":\"test\"}}", Encoding.UTF8, "application/json"));
            Check(valid.IsSuccessStatusCode, "Authenticated bridge call failed.");
            using var oversized = await http.PostAsync(page + "/call", new StringContent(new string('x', 1_000_001)));
            Check(oversized.StatusCode == HttpStatusCode.BadRequest, "Oversized request accepted.");
            using var saved = (await PluginDashboard.WebAssetAsync(handle, "web", "index.html", default)).Value!.Content;
            var waiting = PluginDashboard.WebCallAsync(handle, "web", "wait", null, default);
            await plugin.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var downloading = http.GetAsync(page + "/request?path=%2Fslow", HttpCompletionOption.ResponseHeadersRead);
            await plugin.TransferEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var activeResponse = await downloading;
            var unload = handle.UnloadAsync();
            try { await waiting; throw new Exception("Unload did not cancel call"); } catch (OperationCanceledException) { }
            await unload;
            Check(plugin.TransferCanceled, "Unload failed to cancel active response stream.");
            Check(PluginDashboard.List(handle).Count == 0 && saved.CanRead, "Unload did not remove pages or destroyed host-owned file.");
            await app.StopAsync();
            Console.WriteLine("Web pages: embedded resources, isolation, Bearer authentication, bounds, JSON copying and unload cancellation passed.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task VerifyRequestsAsync(HttpClient http, string page, WebPlugin plugin, PluginContext context)
    {
        string Target(string path) => page + "/request?path=" + Uri.EscapeDataString(path);
        using (var anonymous = new HttpClient { BaseAddress = http.BaseAddress })
        using (var denied = await anonymous.PostAsync(Target("/echo"), new ByteArrayContent([1, 2])))
            Check(denied.StatusCode == HttpStatusCode.Unauthorized, "Raw request bypassed authentication.");
        using (var message = new HttpRequestMessage(HttpMethod.Post, Target("/echo?q=hello%20world")))
        {
            var bytes = Enumerable.Range(0, 1024 * 1024 + 7).Select(i => (byte)(i % 251)).ToArray();
            message.Content = new ByteArrayContent(bytes); message.Content.Headers.ContentType = new("application/octet-stream");
            using var echoed = await http.SendAsync(message);
            Check(echoed.IsSuccessStatusCode && (await echoed.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes), "Binary body was changed.");
            Check(plugin.LastPath == "/echo" && plugin.LastQuery == "hello world", "Path/query did not reach page handler.");
        }
        using (var multipart = new MultipartFormDataContent())
        {
            multipart.Add(new StringContent("猫"), "caption");
            multipart.Add(new ByteArrayContent(Enumerable.Repeat((byte)7, 100_000).ToArray()), "image", "cat.png");
            using var uploaded = await http.PostAsync(Target("/upload"), multipart);
            var form = JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!;
            Check(uploaded.IsSuccessStatusCode && form["length"]!.GetValue<int>() == 100_000 && form["caption"]!.GetValue<string>() == "猫", "Multipart parsing failed.");
        }
        using (var message = new HttpRequestMessage(HttpMethod.Get, Target("/headers")))
        {
            message.Headers.Add("Cookie", "secret=value"); message.Headers.Add("X-Api-Key", "secret");
            message.Headers.Add("X-Correlation-Id", "demo"); message.Headers.Add("X-Forwarded-Host", "host");
            using var response = await http.SendAsync(message);
            var headers = await response.Content.ReadAsStringAsync();
            Check(!headers.Contains("secret") && !headers.Contains("web-test-key") && !headers.Contains("Forwarded") && headers.Contains("demo"), "Host secrets leaked into page request.");
        }
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete, HttpMethod.Options })
        {
            using var result = await http.SendAsync(new HttpRequestMessage(method, Target("/status")));
            Check((int)result.StatusCode == 422, "Business status code lost.");
        }
        using (var range = new HttpRequestMessage(HttpMethod.Get, Target("/video")))
        {
            range.Headers.Range = new RangeHeaderValue(2, 5);
            using var result = await http.SendAsync(range);
            Check(result.StatusCode == HttpStatusCode.PartialContent && (await result.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 2, 3, 4, 5 }) && result.Content.Headers.ContentRange!.From == 2, "Range response was lost.");
        }
        using (var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, Target("/video"))))
            Check(head.Content.Headers.ContentLength == 10 && (await head.Content.ReadAsByteArrayAsync()).Length == 0, "HEAD response failed.");
        using (var unsafeResponse = await http.GetAsync(Target("/response-headers")))
            Check(!unsafeResponse.Headers.Contains("Set-Cookie") && !unsafeResponse.Headers.Contains("Location") && unsafeResponse.Headers.GetValues("Content-Security-Policy").Single().Contains("sandbox"), "Unsafe response headers escaped.");
        foreach (var path in new[] { "https://example.com/", "//example.com/", "/../config", "/%2e%2e/config", "/%252e%252e/config", "/x\\y" })
        {
            using var result = await http.GetAsync(Target(path));
            Check(result.StatusCode == HttpStatusCode.BadRequest, "Unsafe target accepted: " + path);
        }
        using var small = context.Dashboard.AddWebPage("small", "Small", new DashboardWebPage(typeof(WebPlugin).Assembly, "test-web/")
            { RequestAsync = plugin.Request, MaxRequestBytes = 4, MaxResponseBytes = 4 });
        var smallPath = page.Replace("/pages/web/", "/pages/small/") + "/request?path=%2Fecho";
        using (var tooLarge = await http.PostAsync(smallPath, new ByteArrayContent([1, 2, 3, 4, 5])))
            Check(tooLarge.StatusCode == HttpStatusCode.RequestEntityTooLarge, "Known length upload limit ignored.");
        using (var tooLarge = await http.PostAsync(smallPath, new ChunkedContent()))
            Check(tooLarge.StatusCode == HttpStatusCode.RequestEntityTooLarge, "Chunked upload limit ignored.");
        using (var oversized = await http.GetAsync(smallPath.Replace("%2Fecho", "%2Fvideo")))
            Check(oversized.StatusCode == HttpStatusCode.InternalServerError, "Response limit ignored.");
        using (var stream = await http.GetAsync(Target("/stream")))
            Check((await stream.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3 }), "Stream response did not complete.");
        await plugin.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(plugin.StreamDisposed, "Response stream was not disposed.");
    }
    private sealed class WebPlugin : PluginBase
    {
        public JsonObject Result { get; } = new() { ["text"] = "changed" };
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastPath, LastQuery;
        public bool StreamDisposed;
        public bool TransferCanceled;
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TransferEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task LoadAsync()
        {
            Context.Dashboard.AddWebPage("web", "Web", new DashboardWebPage(typeof(WebPlugin).Assembly, "test-web/") { HandleAsync = Handle, RequestAsync = Request });
            using var registration = Context.Dashboard.AddWebPage("temporary", "Temporary", new DashboardWebPage(typeof(WebPlugin).Assembly, "test-web/"));
            return Task.CompletedTask;
        }
        public async Task<IResult> Request(HttpRequest request, CancellationToken ct)
        {
            LastPath = request.Path; LastQuery = request.Query["q"];
            switch (request.Path.Value)
            {
                case "/echo":
                    var output = new MemoryStream(); await request.Body.CopyToAsync(output, ct); output.Position = 0;
                    return Results.Stream(output, request.ContentType);
                case "/upload":
                    var form = await request.ReadFormAsync(ct);
                    return Results.Json(new { length = form.Files[0].Length, caption = form["caption"].ToString() });
                case "/headers": return Results.Json(request.Headers.ToDictionary(p => p.Key, p => p.Value.ToString()));
                case "/video": return Results.File(Enumerable.Range(0, 10).Select(i => (byte)i).ToArray(), "video/mp4", enableRangeProcessing: true);
                case "/response-headers":
                    request.HttpContext.Response.Headers.SetCookie = "bad=cookie";
                    request.HttpContext.Response.Headers.Location = "https://example.com/";
                    request.HttpContext.Response.Headers.ContentSecurityPolicy = "default-src *";
                    return Results.Text("ok");
                case "/stream": return Results.Stream(new DisposalStream(() => { StreamDisposed = true; Disposed.TrySetResult(); }), "application/octet-stream");
                case "/slow": return Results.Stream(async output =>
                {
                    await output.WriteAsync(new byte[] { 1 }, ct); await output.FlushAsync(ct);
                    TransferEntered.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { TransferCanceled = true; throw; }
                }, "application/octet-stream");
                default: return Results.Json(new { error = "validation" }, statusCode: 422);
            }
        }
        private async Task<JsonNode?> Handle(string action, JsonNode? args, CancellationToken ct)
        {
            if (action == "wait") { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            if (action == "large") return JsonValue.Create(new string('x', 1_000_001));
            if (args is JsonObject obj) obj["text"] = "mutated";
            return Result;
        }
    }
    private sealed class DisposalStream(Action disposed) : MemoryStream(new byte[] { 1, 2, 3 })
    {
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) disposed(); }
    }
    private sealed class ChunkedContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }).AsTask();
    }
}
