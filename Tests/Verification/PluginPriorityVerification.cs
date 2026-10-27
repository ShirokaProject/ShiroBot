using System.Reflection;
using System.Reflection.Emit;
using ShiroBot.Adapters;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

internal static class PluginPriorityVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shirobot-priority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VerifyProbe(root);
            await VerifyConfigAsync(root);
            await VerifyDispatchAsync(root);
            await VerifyTimeoutAsync(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyProbe(string root)
    {
        var resolver = new SharedAssemblyResolver();
        var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
        var plugins = new PluginManager(context, resolver, new ModelPackageRegistry(resolver),
            new HostRuntimeState(DateTimeOffset.UtcNow), new HostLogHub()) { PluginRootPath = root };
        var declared = plugins.TryProbePluginInfoFile(typeof(ShiroBot.SharedContractPluginProbe.SharedContractPluginProbe).Assembly.Location);
        var legacy = plugins.TryProbePluginInfoFile(EmitPluginWithoutPriority(root));
        if (declared?.Priority != 5 || legacy?.Priority != BotPluginAttribute.DefaultPriority)
            throw new InvalidOperationException($"Plugin priority probe read {declared?.Priority}/{legacy?.Priority}, expected 5/2.");
        Console.WriteLine("Plugin priority attribute probe verification passed.");
    }

    private static async Task VerifyConfigAsync(string root)
    {
        var path = Path.Combine(root, "config.toml");
        var manager = new ConfigManager(path);
        manager.SaveConfig(path, new CoreConfig());
        manager.SetConfigValue(path, "plugin_routes.priorities.ai-chat", 10);
        manager.SetConfigValue(path, "plugin_routes.priorities.Guard", -1);
        var loaded = await manager.LoadCoreConfig();
        var routes = new PluginRouteConfig();
        routes.CopyFrom(loaded.PluginRoutes);
        // Only a priority was set: the plugin's group routing must stay on the default rule.
        if (routes.GetPriority("AI-CHAT") != 10 || routes.GetPriority("guard") != -1 || routes.GetPriority("other") is not null ||
            routes.Plugins.Count != 0 || !routes.AllowsGroup("ai-chat", "10001"))
            throw new InvalidOperationException("Plugin priority overrides did not round-trip through config.toml.");
        manager.RemoveConfigValue(path, "plugin_routes.priorities.ai-chat");
        if ((await manager.LoadCoreConfig()).PluginRoutes.Priorities.ContainsKey("ai-chat"))
            throw new InvalidOperationException("Removing a plugin priority override left it in config.toml.");
        Console.WriteLine("Plugin priority config round-trip verification passed.");
    }

    /// <summary>A plugin DLL whose attribute has no Priority argument, as in every plugin built before it existed.</summary>
    private static string EmitPluginWithoutPriority(string root)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("LegacyPriorityProbe"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("LegacyPriorityProbe").DefineType("LegacyPriorityProbe.Plugin", TypeAttributes.Public | TypeAttributes.Sealed);
        var attribute = typeof(BotPluginAttribute);
        type.SetCustomAttribute(new CustomAttributeBuilder(attribute.GetConstructor([typeof(string)])!, ["legacy-priority-probe"],
            [attribute.GetProperty(nameof(BotPluginAttribute.Version))!], ["1.0.0"]));
        type.CreateType();
        var path = Path.Combine(root, "LegacyPriorityProbe.dll");
        assembly.Save(path);
        return path;
    }

    private static async Task VerifyDispatchAsync(string root)
    {
        var logs = new HostLogHub();
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var dispatcher = new HostEventDispatcher(new Lock(), bot.ReplySubscriptions, new HostRuntimeState(DateTimeOffset.UtcNow), logs);
        var routes = new PluginRouteConfig();
        var received = new List<string>();

        await RegisterAsync(dispatcher, bot, root, logs, routes, "filter", 0, new RecordingPlugin(received, "filter", filter: true));
        await RegisterAsync(dispatcher, bot, root, logs, routes, "command", 2, new RecordingPlugin(received, "command", command: "help"));
        await RegisterAsync(dispatcher, bot, root, logs, routes, "logger", 2, new RecordingPlugin(received, "logger"));
        await RegisterAsync(dispatcher, bot, root, logs, routes, "ai", 10, new RecordingPlugin(received, "ai"));
        dispatcher.MarkInitialPluginsReady();

        async Task<string[]> Send(string text, params MessageSegment[] extra)
        {
            lock (received) received.Clear();
            await dispatcher.PublishAsync(Message(text, extra));
            lock (received) return received.Order(StringComparer.Ordinal).ToArray();
        }

        // A matched command stops larger priorities; the same priority still sees the message.
        Expect(await Send("help"), "command:help", "filter:help", "logger:help");
        // Nothing handled it: the fallback answers, after every smaller priority finished.
        Expect(await Send("hello"), "ai:hello", "filter:hello", "logger:hello");
        lock (received) if (received[^1] != "ai:hello") throw new InvalidOperationException("Fallback ran before smaller priorities: " + string.Join(",", received));
        // A filter that marks the message blocks everything after it.
        Expect(await Send("spam"), "filter:spam");

        // The owner's override replaces the declared priority and takes effect without re-registering.
        routes.Priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["ai"] = 1 };
        Expect(await Send("help"), "ai:help", "command:help", "filter:help", "logger:help");
        routes.Priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Expect(await Send("help"), "command:help", "filter:help", "logger:help");

        // A reply that answered a subscription reaches ordinary plugins but not fallbacks.
        var reference = new MessageReference("qq", Channel.Direct("owner"), "notice");
        using (bot.ReplySubscriptions.Subscribe("host", reference, TimeSpan.FromMinutes(1), _ => Task.CompletedTask, disposeOnReply: false))
            Expect(await Send("确认", new QuoteSegment("notice")), "filter:确认", "logger:确认");
        Expect(await Send("确认", new QuoteSegment("notice")), "ai:确认", "filter:确认", "logger:确认");
        Console.WriteLine("Plugin priority ordering, handled marks, owner overrides and reply handling verification passed.");
    }

    private static async Task VerifyTimeoutAsync(string root)
    {
        var logs = new HostLogHub();
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var dispatcher = new HostEventDispatcher(new Lock(), bot.ReplySubscriptions, new HostRuntimeState(DateTimeOffset.UtcNow), logs)
        {
            PriorityLevelTimeout = TimeSpan.FromMilliseconds(100)
        };
        var routes = new PluginRouteConfig();
        var received = new List<string>();
        var slow = new RecordingPlugin(received, "slow");
        await RegisterAsync(dispatcher, bot, root, logs, routes, "slow", 2, slow);
        await RegisterAsync(dispatcher, bot, root, logs, routes, "ai", 10, new RecordingPlugin(received, "ai"));
        dispatcher.MarkInitialPluginsReady();

        var publish = dispatcher.PublishAsync(Message("hello"));
        await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!received.Contains("ai:hello") && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!received.Contains("ai:hello") || publish.IsCompleted)
            throw new InvalidOperationException("A slow priority held back later ones, or dispatch stopped waiting for it.");
        slow.Release.SetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine("Plugin priority level timeout verification passed.");
    }

    private static async Task RegisterAsync(
        HostEventDispatcher dispatcher, BotContext bot, string root, HostLogHub logs, PluginRouteConfig routes,
        string id, int priority, IBotPlugin plugin)
    {
        var context = new PluginContext(bot, id, Path.Combine(root, id), logs, new PluginServiceRegistry());
        await plugin.OnLoad(context);
        dispatcher.RegisterPlugin(new LoadedPluginHandle(plugin, context, new DllLoader<IBotPlugin>(), typeof(PluginPriorityVerification).Assembly.Location,
            new PluginProbeInfo(plugin.GetType().FullName!, id, id, "1.0.0", null, null, PluginCategory.Other, null, false, [], [], priority),
            logs, priorityOverride: () => routes.GetPriority(id)));
    }

    private static MessageEvent Message(string text, params MessageSegment[] extra) => new()
    {
        Platform = "qq", InstanceId = "qq", MessageId = Guid.NewGuid().ToString("N"),
        Channel = Channel.Direct("owner"), Sender = new User("owner"),
        Segments = [.. extra, new TextSegment(text)]
    };

    private static void Expect(string[] actual, params string[] expected)
    {
        if (!actual.SequenceEqual(expected.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException($"Priority dispatch delivered [{string.Join(",", actual)}], expected [{string.Join(",", expected)}].");
    }

    /// <summary>Records what it receives: a command route, a filter that marks "spam", or (by default) MapAll.</summary>
    private sealed class RecordingPlugin(List<string> received, string name, string? command = null, bool filter = false) : PluginBase
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Name => name;

        protected override void ConfigureRoutes()
        {
            if (command is not null) DirectCommands.MapExact(command, Record);
            else if (!filter) DirectCommands.MapAll(Record);
        }

        protected override async Task OnDirectMessageAsync(MessageEvent message)
        {
            if (filter)
            {
                await Record(message);
                if (message.GetPlainText().Trim() == "spam") message.MarkHandled();
                return;
            }

            await base.OnDirectMessageAsync(message);
        }

        private async Task Record(MessageEvent message)
        {
            lock (received) received.Add($"{name}:{message.GetPlainText().Trim()}");
            if (name != "slow") return;
            Started.TrySetResult();
            await Release.Task;
        }
    }
}
