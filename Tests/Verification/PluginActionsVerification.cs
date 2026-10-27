using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Logging;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using CH = ShiroBot.TerminalUI.ConsoleOutput;

internal static class PluginActionsVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shirobot-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logs = new HostLogHub();
            var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
            var plugin = new ActionPlugin();
            var context = new PluginContext(bot, "actions", Path.Combine(root, "actions"), logs, new PluginServiceRegistry());
            await plugin.OnLoad(context);
            var handle = new LoadedPluginHandle(plugin, context, new DllLoader<IBotPlugin>(), typeof(PluginActionsVerification).Assembly.Location,
                new PluginProbeInfo(typeof(ActionPlugin).FullName!, "actions", "Actions", "1.0.0", null, null, PluginCategory.Other, null, false, [], []),
                logs);

            var listed = await PluginActions.ListAsync(handle);
            if (listed is null || !listed.Select(action => action.Id).SequenceEqual(["report", "broken"]))
                throw new InvalidOperationException("Plugin actions were not filtered and de-duplicated: " + string.Join(",", listed?.Select(a => a.Id) ?? []));

            var report = await PluginActions.ExecuteAsync(handle, "REPORT");
            if (report is not { Status: PluginActionStatus.Done, Message: "已生成", Result.Details: "第一行\n第二行", Action.Label: "生成报告" })
                throw new InvalidOperationException("Action details did not reach the caller.");

            // A throwing plugin is a failed outcome naming the reason, not an exception for the console loop.
            var broken = await PluginActions.ExecuteAsync(handle, "broken");
            if (broken.Status != PluginActionStatus.Failed || !broken.Message.Contains("磁盘已满", StringComparison.Ordinal))
                throw new InvalidOperationException("A throwing action did not report its reason: " + broken.Message);
            if ((await PluginActions.ExecuteAsync(handle, "missing")).Status != PluginActionStatus.ActionNotFound)
                throw new InvalidOperationException("A missing action was not reported as not found.");

            CH.Panel("生成报告", "第一行：中文宽度按两列计算\n第二行 with ASCII");
            Console.WriteLine("Plugin action listing, details, failures and console panel verification passed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ActionPlugin : PluginBase, IPluginActionProvider
    {
        public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
        [
            new("report", "生成报告"),
            new("REPORT", "重复的 ID"),
            new("", "没有 ID"),
            new("broken", "会出错")
        ];

        public Task<PluginActionResult> ExecuteActionAsync(string actionId, CancellationToken cancellationToken = default) =>
            actionId == "broken"
                ? throw new IOException("磁盘已满")
                : Task.FromResult(new PluginActionResult(true, "已生成") { Details = "第一行\n第二行" });
    }
}
