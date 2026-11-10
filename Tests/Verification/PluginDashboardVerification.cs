using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Logging;
using ShiroBot.InteropContractProbe;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Dashboard;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Verification;

internal static class PluginDashboardVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shirobot-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DashboardRule? submittedRule = null;
            var typedForm = Ui.Form<DashboardRule>("typed").Submit("保存", (rule, _, _) =>
            {
                submittedRule = rule;
                return Task.FromResult(DashboardResult.Done());
            });
            Check(
                typedForm.Fields is
                [
                    { Kind: UiFieldKind.Text, MaxLength: 10, Required: true }, { Kind: UiFieldKind.Group },
                    { Kind: UiFieldKind.Number, Min: 0, Max: 3600 }, { Kind: UiFieldKind.Switch }
                ], "Record form schema lost field hints or bounds.");
            var (typedValues, typedErrors) = FormValidator.Validate(typedForm,
                JsonNode.Parse("""{"name":"猫","group":"123","cooldown":30,"enabled":true}""")!.AsObject());
            Check(typedErrors.Count == 0, "Valid typed form rejected.");
            await typedForm.Invoke(new DashboardForm(typedValues),
                new DashboardEvent(new DashboardContext("test", new Dictionary<string, string>()), "typed"),
                CancellationToken.None);
            Check(submittedRule is { Name: "猫", Group: "123", Cooldown: 30, Enabled: true },
                "Typed form did not deserialize.");
            var logs = new HostLogHub();
            var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
            var plugin = new PagePlugin(Path.Combine(root, "image.png"));
            await File.WriteAllBytesAsync(plugin.ImagePath, [1, 2, 3]);
            var context = new PluginContext(bot, "pages", Path.Combine(root, "pages"), logs,
                new PluginServiceRegistry());
            await plugin.OnLoad(context);
            var handle = new LoadedPluginHandle(plugin, context, new DllLoader<IBotPlugin>(),
                typeof(PluginDashboardVerification).Assembly.Location,
                new PluginProbeInfo(typeof(PagePlugin).FullName!, "pages", "Pages", "1.0.0", null, null,
                    PluginCategory.Other, null, false, [], []), logs);
            Check(PluginDashboard.List(handle).Select(p => p.Id).SequenceEqual(["stats", "library"]),
                "Pages must be ordered.");
            try
            {
                context.Dashboard.AddPage("LIBRARY", "duplicate", plugin.Render);
                throw new Exception("Duplicate accepted");
            }
            catch (InvalidOperationException)
            {
            }

            var state = new Dictionary<string, string> { ["q"] = " 猫 ", ["page"] = "2" };
            var rendered = await PluginDashboard.RenderAsync(handle, "LIBRARY", state, CancellationToken.None);
            Check(
                rendered.Status == PluginPageStatus.Done && plugin.LastContext is { } c && c.Filter("q") == "猫" &&
                c.Page() == 2,
                "Render must receive page state and serialize components.");
            var children = rendered.Value!.View["children"]!.AsArray();
            var collectionId = children[0]!["id"]!.GetValue<string>();
            var removed = await PluginDashboard.EventAsync(handle, "library", state,
                new PluginPageEvent("action", collectionId, "delete", "m1"), CancellationToken.None);
            Check(
                removed is { Status: PluginPageStatus.Done, Value.Result.Ok: true, Value.View: not null } &&
                plugin.Deleted == "m1", "Declared action failed.");
            foreach (var e in new[]
                     {
                         new PluginPageEvent("action", collectionId, "format-disk", "m1"),
                         new("action", collectionId, "delete", "missing"),
                         new("action", collectionId, "delete", "protected"), new("click", "missing")
                     })
                Check(
                    (await PluginDashboard.EventAsync(handle, "library", state, e, CancellationToken.None)).Status ==
                    PluginPageStatus.Stale && plugin.DeleteCalls == 1, "Stale actions reached plugin.");
            var invalid = await PluginDashboard.EventAsync(handle, "library", state,
                new PluginPageEvent("submit", "edit",
                    Values: new JsonObject { ["name"] = "", ["count"] = 1.5, ["choice"] = "unknown" }),
                CancellationToken.None);
            Check(invalid is { Status: PluginPageStatus.InvalidForm, Errors.Count: 3 } && plugin.SubmitCalls == 0,
                "Invalid form reached plugin.");
            var valid = await PluginDashboard.EventAsync(handle, "library", state,
                new PluginPageEvent("submit", "edit",
                    Values: new JsonObject { ["name"] = "猫", ["count"] = 2, ["choice"] = "a", ["undeclared"] = "no" }),
                CancellationToken.None);
            Check(
                valid is { Status: PluginPageStatus.Done, Value.Result.Ok: true } &&
                plugin.Submitted is { Count: 3 } values && values["count"]!.GetValue<long>() == 2,
                "Form must validate and remove undeclared fields.");
            var file = await PluginDashboard.OpenFileAsync(handle, "library", "m1", CancellationToken.None);
            await using var fileStream = file.Value?.Content ?? throw new InvalidOperationException("File not opened.");
            var bytes = await PluginDashboard.OpenFileAsync(handle, "library", "inline", CancellationToken.None);
            await using var inline = bytes.Value!.Content;
            plugin.Inline[0] = 99;
            using var copied = new MemoryStream();
            await inline.CopyToAsync(copied);
            Check(copied.ToArray().SequenceEqual(new byte[] { 4, 5 }), "Inline file must be copied.");
            foreach (var id in new[] { "missing", "deleted-path" })
                Check(
                    (await PluginDashboard.OpenFileAsync(handle, "library", id, CancellationToken.None)).Status ==
                    PluginPageStatus.NotFound, "Missing file must be 404.");
            using (var uploadBody = new MemoryStream([7, 8, 9]))
            {
                var uploaded = await PluginDashboard.UploadAsync(handle, "library", state, "import", "../../image.png",
                    "image/png", uploadBody, 3, CancellationToken.None);
                Check(
                    uploaded is { Status: PluginPageStatus.Done, Value.Result.Ok: true } &&
                    plugin.LastUpload is { FileName: "image.png", ContentType: "image/png" } &&
                    plugin.LastUpload.Content.Span.SequenceEqual(new byte[] { 7, 8, 9 }),
                    "Upload must be bounded and sanitized.");
            }

            foreach (var (element, name, mime, contents, length, expected) in new (string, string, string, byte[], long?
                         , PluginPageStatus)[]
                         {
                             ("import", "x.png", "image/png", [1], 5, PluginPageStatus.UploadTooLarge),
                             ("import", "x.png", "image/png", [1, 2, 3, 4, 5], null, PluginPageStatus.UploadTooLarge),
                             ("import", "x.png", "image/png", [], 0, PluginPageStatus.InvalidUpload),
                             ("import", "", "image/png", [1], 1, PluginPageStatus.InvalidUpload),
                             ("import", "x.txt", "text/plain", [1], 1, PluginPageStatus.InvalidUpload),
                             ("missing", "x.png", "image/png", [1], 1, PluginPageStatus.Stale)
                         })
            {
                using var input = new MemoryStream(contents);
                Check(
                    (await PluginDashboard.UploadAsync(handle, "library", state, element, name, mime, input, length,
                        CancellationToken.None)).Status == expected && plugin.UploadCalls == 1,
                    "Invalid upload reached plugin.");
            }

            using (var cancellation = new CancellationTokenSource())
            using (var input = new MemoryStream([1]))
            {
                await cancellation.CancelAsync();
                try
                {
                    await PluginDashboard.UploadAsync(handle, "library", state, "import", "x.png", "image/png", input,
                        1, cancellation.Token);
                    throw new Exception("Cancellation swallowed");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }

            Check(
                (await PluginDashboard.RenderAsync(handle, "stats", [], CancellationToken.None)).Status ==
                PluginPageStatus.Failed, "Plugin failure must be reported.");
            Check(
                (await PluginDashboard.RenderAsync(handle, "missing", [], CancellationToken.None)).Status ==
                PluginPageStatus.NotFound, "Missing page must be 404.");
            await handle.UnloadAsync();
            Check(PluginDashboard.List(handle).Count == 0, "Unload must remove registered pages.");
            await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = services, Request = { Method = "GET" } };
            using var body = new MemoryStream();
            http.Response.Body = body;
            await Results.File(fileStream, file.Value!.ContentType, enableRangeProcessing: true).ExecuteAsync(http);
            Check(body.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }) && !fileStream.CanRead,
                "HTTP file must survive unload and dispose its stream.");
            Console.WriteLine(
                "Plugin dashboard registration, component rendering, events, forms, uploads and lifetime verification passed.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PagePlugin(string imagePath) : PluginBase
    {
        public string ImagePath { get; } = imagePath;
        public byte[] Inline { get; } = [4, 5];
        public DashboardContext? LastContext { get; private set; }
        public string? Deleted { get; private set; }
        public int DeleteCalls { get; private set; }
        public int SubmitCalls { get; private set; }
        public JsonObject? Submitted { get; private set; }
        public DashboardUploadFile? LastUpload { get; private set; }
        public int UploadCalls { get; private set; }

        protected override Task LoadAsync()
        {
            Context.Dashboard.AddPage("library", "表情库", Render, order: 20, files: OpenFile);
            Context.Dashboard.AddPage("stats", "统计", (_, _) => throw new IOException("统计库坏了"), order: 10);
            return Task.CompletedTask;
        }

        public Task<UiNode> Render(DashboardContext page, CancellationToken ct)
        {
            LastContext = page;
            return Task.FromResult(Ui.Column(
                Ui.Gallery(["m1", "protected"], id => new UiCard(id, "猫猫") { Image = id })
                    .Action("delete", "删除", (id, _, _) =>
                    {
                        DeleteCalls++;
                        Deleted = id;
                        return Task.FromResult(DashboardResult.Done("已删除"));
                    }, when: id => id == "m1"),
                Ui.Upload("import", "上传图片", (file, _, _) =>
                {
                    UploadCalls++;
                    LastUpload = file;
                    return Task.FromResult(DashboardResult.Done("已上传"));
                }, ["image/png"], 4),
                Ui.Form("edit").Text("name", "名称", true, 10).Number("count", "数量", 1, 5, true)
                    .Select("choice", "选择", [("a", "A")])
                    .Submit("保存", (form, _, _) =>
                    {
                        Check(form.Get<int>("count") == 2, "DashboardForm must read validated integers.");
                        SubmitCalls++;
                        Submitted = form.Values;
                        return Task.FromResult(DashboardResult.Done("保存"));
                    })));
        }

        protected override Task OnUnloadAsync()
        {
            File.Delete(ImagePath);
            return Task.CompletedTask;
        }

        private Task<DashboardFile?> OpenFile(string id, CancellationToken ct)
        {
            return Task.FromResult(id switch
            {
                "m1" => DashboardFile.FromPath(ImagePath, "image/png"),
                "deleted-path" => DashboardFile.FromPath(ImagePath + ".missing", "image/png"),
                "inline" => DashboardFile.FromBytes(Inline, "image/png"),
                _ => null
            });
        }
    }
}