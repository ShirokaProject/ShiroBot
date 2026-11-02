using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Logging;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

/// <summary>插件页面：页面过滤、分页和筛选、只执行声明过的操作、文件、插件出错。</summary>
internal static class PluginDashboardVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shirobot-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logs = new HostLogHub();
            var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
            var plugin = new PagePlugin(Path.Combine(root, "image.png"));
            await File.WriteAllBytesAsync(plugin.ImagePath, [1, 2, 3]);
            var context = new PluginContext(bot, "pages", Path.Combine(root, "pages"), logs, new PluginServiceRegistry());
            await plugin.OnLoad(context);
            var handle = new LoadedPluginHandle(plugin, context, new DllLoader<IBotPlugin>(), typeof(PluginDashboardVerification).Assembly.Location,
                new PluginProbeInfo(typeof(PagePlugin).FullName!, "pages", "Pages", "1.0.0", null, null, PluginCategory.Other, null, false, [], []),
                logs);

            var pages = await PluginDashboard.ListAsync(handle);
            if (pages is null || !pages.Select(page => page.Id).SequenceEqual(["stats", "library"]))
                throw new InvalidOperationException("Pages were not filtered, de-duplicated and ordered: " + string.Join(",", pages?.Select(p => p.Id) ?? []));

            var data = await PluginDashboard.QueryAsync(handle, "LIBRARY", 2, 500,
                new Dictionary<string, string> { ["q"] = " 猫 ", ["undeclared"] = "x", ["tag"] = " " }, CancellationToken.None);
            if (data is not { Status: PluginPageStatus.Done, Value.Items: [{ Id: "m1" }] } || plugin.LastQuery is not { Page: 2, PageSize: 100 } query ||
                !query.Filters.SequenceEqual([new KeyValuePair<string, string>("q", "猫")]))
                throw new InvalidOperationException("Queries must clamp paging and pass only declared, non-empty filters.");

            var deleted = await PluginDashboard.RunActionAsync(handle, "library", "DELETE", "m1", CancellationToken.None);
            if (deleted is not { Status: PluginPageStatus.Done, Value: { Ok: true, Refresh: true } } || plugin.Deleted != "m1")
                throw new InvalidOperationException("A declared action did not run on its item.");
            if ((await PluginDashboard.RunActionAsync(handle, "library", "format-disk", "m1", CancellationToken.None)).Status != PluginPageStatus.NotFound)
                throw new InvalidOperationException("An undeclared action must not reach the plugin.");

            foreach (var itemId in new[] { "protected", "missing", "mismatched", "no-actions" })
            {
                if ((await PluginDashboard.RunActionAsync(handle, "library", "delete", itemId, CancellationToken.None)).Status != PluginPageStatus.NotFound ||
                    plugin.Deleted != "m1")
                    throw new InvalidOperationException("Unsupported item actions must not reach the plugin: " + itemId);
            }

            var file = await PluginDashboard.OpenFileAsync(handle, "library", "m1", CancellationToken.None);
            using var fileStream = file.Value?.Content ?? throw new InvalidOperationException("A file was not opened before leaving plugin dispatch.");
            if (file.Value!.ContentType != "image/png" || !fileStream.CanRead)
                throw new InvalidOperationException("A file on disk was not returned as an open stream.");
            var bytes = await PluginDashboard.OpenFileAsync(handle, "library", "inline", CancellationToken.None);
            using var inline = bytes.Value?.Content ?? throw new InvalidOperationException("Inline content was not copied.");
            plugin.Inline[0] = 99;
            using var copied = new MemoryStream();
            await inline.CopyToAsync(copied);
            if (!copied.ToArray().SequenceEqual(new byte[] { 4, 5 }))
                throw new InvalidOperationException("Inline file contents still depend on plugin-owned bytes.");
            foreach (var id in new[] { "missing", "deleted-path" })
                if ((await PluginDashboard.OpenFileAsync(handle, "library", id, CancellationToken.None)).Status != PluginPageStatus.NotFound)
                    throw new InvalidOperationException("A missing file must be reported as not found.");

            using (var uploadBody = new MemoryStream([7, 8, 9]))
            {
                var uploaded = await PluginDashboard.UploadAsync(handle, "library", "../../image.png", "image/png", uploadBody, 3, CancellationToken.None);
                if (uploaded is not { Status: PluginPageStatus.Done, Value: { Ok: true, Refresh: true } } ||
                    plugin.LastUpload is not { FileName: "image.png", ContentType: "image/png" } ||
                    !plugin.LastUpload.Content.Span.SequenceEqual(new byte[] { 7, 8, 9 }))
                    throw new InvalidOperationException("Declared uploads must run inside plugin dispatch with a safe display name and bounded contents.");
            }
            foreach (var (pageId, name, mime, contents, length, expected) in new[]
            {
                ("library", "x.png", "image/png", new byte[] { 1 }, (long?)5, PluginPageStatus.UploadTooLarge),
                ("library", "x.png", "image/png", new byte[] { 1, 2, 3, 4, 5 }, (long?)null, PluginPageStatus.UploadTooLarge),
                ("library", "x.png", "image/png", Array.Empty<byte>(), (long?)0, PluginPageStatus.InvalidUpload),
                ("library", "", "image/png", new byte[] { 1 }, (long?)1, PluginPageStatus.InvalidUpload),
                ("library", "x.txt", "text/plain", new byte[] { 1 }, (long?)1, PluginPageStatus.InvalidUpload),
                ("stats", "x.png", "image/png", new byte[] { 1 }, (long?)1, PluginPageStatus.NotFound),
            })
            {
                using var bodyInput = new MemoryStream(contents);
                var outcome = await PluginDashboard.UploadAsync(handle, pageId, name, mime, bodyInput, length, CancellationToken.None);
                if (outcome.Status != expected || plugin.UploadCalls != 1)
                    throw new InvalidOperationException("Invalid or undeclared uploads reached the plugin: " + outcome.Status);
            }
            using (var cancellation = new CancellationTokenSource())
            using (var cancelledBody = new MemoryStream([1]))
            {
                cancellation.Cancel();
                try
                {
                    await PluginDashboard.UploadAsync(handle, "library", "x.png", "image/png", cancelledBody, 1, cancellation.Token);
                    throw new InvalidOperationException("Upload cancellation was swallowed.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }

            var broken = await PluginDashboard.QueryAsync(handle, "stats", 1, 0, new Dictionary<string, string>(), CancellationToken.None);
            if (broken.Status != PluginPageStatus.Failed || !broken.Message.Contains("统计库坏了", StringComparison.Ordinal))
                throw new InvalidOperationException("A throwing page did not report its reason: " + broken.Message);
            if ((await PluginDashboard.QueryAsync(handle, "missing", 1, 0, new Dictionary<string, string>(), CancellationToken.None)).Status != PluginPageStatus.NotFound)
                throw new InvalidOperationException("A missing page must be reported as not found.");

            await handle.UnloadAsync();
            if (File.Exists(plugin.ImagePath)) throw new InvalidOperationException("The unload fixture did not delete its file.");
            using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.Method = "GET";
            using var body = new MemoryStream();
            http.Response.Body = body;
            await Results.File(fileStream, file.Value.ContentType, enableRangeProcessing: true).ExecuteAsync(http);
            if (!body.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }) || fileStream.CanRead)
                throw new InvalidOperationException("HTTP response must survive plugin file deletion and dispose its stream.");

            Console.WriteLine("Plugin dashboard page listing, queries, actions, files and failures verification passed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PagePlugin(string imagePath) : PluginBase, IPluginDashboardProvider
    {
        public string ImagePath { get; } = imagePath;
        public byte[] Inline { get; } = [4, 5];
        public DashboardQuery? LastQuery { get; private set; }
        public string? Deleted { get; private set; }
        public DashboardUploadFile? LastUpload { get; private set; }
        public int UploadCalls { get; private set; }

        public IReadOnlyList<DashboardPage> Pages { get; } =
        [
            new("library", "表情库", DashboardPageKinds.Gallery, Order: 20,
                Filters: [new DashboardFilter("q", "搜索"), new DashboardFilter("tag", "标签")],
                Actions: [new DashboardAction("delete", "删除", "确定删除？", "danger")])
                { Upload = new DashboardUpload("上传图片", ["image/png"], 4) },
            new("LIBRARY", "重复", DashboardPageKinds.Gallery),
            new("stats", "统计", DashboardPageKinds.Stats, Order: 10),
            new("html", "注入页面", "iframe"),
            new("", "没有 ID", DashboardPageKinds.List)
        ];

        public Task<DashboardPageData> QueryAsync(string pageId, DashboardQuery query, CancellationToken cancellationToken = default)
        {
            if (pageId == "stats") throw new IOException("统计库坏了");
            LastQuery = query;
            return Task.FromResult(new DashboardPageData([new DashboardItem("m1", "猫猫", ImageFileId: "m1", Actions: ["delete"])], 1));
        }

        public Task<DashboardItem?> GetItemAsync(string pageId, string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DashboardItem?>(itemId switch
            {
                "m1" => new DashboardItem("m1", "猫猫", Actions: ["DELETE"]),
                "protected" => new DashboardItem("protected", "保护条目", Actions: []),
                "mismatched" => new DashboardItem("different-id", "错误的 ID", Actions: ["delete"]),
                "no-actions" => new DashboardItem("no-actions", "只读条目"),
                _ => null
            });

        protected override Task OnUnloadAsync()
        {
            File.Delete(ImagePath);
            return Task.CompletedTask;
        }

        public Task<PluginActionResult> RunActionAsync(string pageId, string actionId, string itemId, CancellationToken cancellationToken = default)
        {
            Deleted = itemId;
            return Task.FromResult(new PluginActionResult(true, "已删除", Refresh: true));
        }

        public Task<PluginActionResult> UploadAsync(string pageId, DashboardUploadFile file, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            LastUpload = file;
            return Task.FromResult(new PluginActionResult(true, "已上传", Refresh: true));
        }

        public Task<DashboardFile?> OpenFileAsync(string pageId, string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(fileId switch
            {
                "m1" => DashboardFile.FromPath(ImagePath, "image/png"),
                "deleted-path" => DashboardFile.FromPath(ImagePath + ".missing", "image/png"),
                "inline" => DashboardFile.FromBytes(Inline, "image/png"),
                _ => null
            });
    }
}
