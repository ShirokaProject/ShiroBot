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

            var file = await PluginDashboard.OpenFileAsync(handle, "library", "m1", CancellationToken.None);
            if (file.Value is not { Path: { } path, ContentType: "image/png" } || path != plugin.ImagePath)
                throw new InvalidOperationException("A file on disk was not returned by path.");
            var bytes = await PluginDashboard.OpenFileAsync(handle, "library", "inline", CancellationToken.None);
            if (bytes.Value?.Content is not [4, 5] || ReferenceEquals(bytes.Value.Content, plugin.Inline))
                throw new InvalidOperationException("File contents must be copied out of the plugin.");
            if ((await PluginDashboard.OpenFileAsync(handle, "library", "missing", CancellationToken.None)).Status != PluginPageStatus.NotFound)
                throw new InvalidOperationException("A missing file must be reported as not found.");

            var broken = await PluginDashboard.QueryAsync(handle, "stats", 1, 0, new Dictionary<string, string>(), CancellationToken.None);
            if (broken.Status != PluginPageStatus.Failed || !broken.Message.Contains("统计库坏了", StringComparison.Ordinal))
                throw new InvalidOperationException("A throwing page did not report its reason: " + broken.Message);
            if ((await PluginDashboard.QueryAsync(handle, "missing", 1, 0, new Dictionary<string, string>(), CancellationToken.None)).Status != PluginPageStatus.NotFound)
                throw new InvalidOperationException("A missing page must be reported as not found.");

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

        public IReadOnlyList<DashboardPage> Pages { get; } =
        [
            new("library", "表情库", DashboardPageKinds.Gallery, Order: 20,
                Filters: [new DashboardFilter("q", "搜索"), new DashboardFilter("tag", "标签")],
                Actions: [new DashboardAction("delete", "删除", "确定删除？", "danger")]),
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

        public Task<PluginActionResult> RunActionAsync(string pageId, string actionId, string itemId, CancellationToken cancellationToken = default)
        {
            Deleted = itemId;
            return Task.FromResult(new PluginActionResult(true, "已删除", Refresh: true));
        }

        public Task<DashboardFile?> OpenFileAsync(string pageId, string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(fileId switch
            {
                "m1" => DashboardFile.FromPath(ImagePath, "image/png"),
                "inline" => DashboardFile.FromBytes(Inline, "image/png"),
                _ => null
            });
    }
}
