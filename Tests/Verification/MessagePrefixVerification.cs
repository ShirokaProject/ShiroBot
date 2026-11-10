using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Verification;

internal static class MessagePrefixVerification
{
    private static readonly JsonSerializerOptions SchemaJsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync()
    {
        if (new CoreConfig().PluginRoutes.RequireMessagePrefix)
            throw new Exception("Prefix requirement must be off by default");
        VerifyViews();
        var root = Path.Combine(Path.GetTempPath(), "shirobot-prefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await VerifyConfigAsync(root);
            await VerifyDispatchAsync(root);
            await VerifyObserverRoutesAsync(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }

        Console.WriteLine(
            "PASS message prefix: group/DM gates, per-plugin inheritance/overrides, hot config, commands, shared handled state, media, replies and config API.");
    }

    private static void VerifyViews()
    {
        var quote = new QuoteSegment("quoted");
        var mention = new MentionSegment("bot");
        var image = new ImageSegment("file:///fixture.png");
        var raw = new object();
        var source = Message("", false) with
        {
            Raw = raw, Segments = [quote, new TextSegment(" \t!"), mention, new TextSegment("! \n#help"), image]
        };
        var view = MessageTriggerPrefix.Strip(source, "!!") ?? throw new Exception("Split prefix did not match");
        if (view.GetPlainText() != "#help" || source.GetPlainText() != " \t!! \n#help" ||
            !ReferenceEquals(view.Raw, raw) || view.MessageId != source.MessageId || view.Sender != source.Sender ||
            !view.Segments.SequenceEqual([quote, mention, new TextSegment("#help"), image]))
            throw new Exception("Prefix stripping changed metadata/media or mutated the original");
        view.MarkHandled();
        if (!source.IsHandled() || (source with { }).IsHandled())
            throw new Exception("Handled state did not propagate, or leaked into an ordinary record clone");
        var fresh = Message("!hello", true);
        var stripped = MessageTriggerPrefix.Strip(fresh, "!")!;
        fresh.MarkHandled();
        if (!stripped.IsHandled()) throw new Exception("Original handled state did not propagate to its view");
        if (MessageTriggerPrefix.Strip(Message("hello!", true), "!") is not null ||
            MessageTriggerPrefix.Strip(Message("bot: hi", true), "BOT:") is not null)
            throw new Exception("Matched a non-leading or wrong-case literal prefix");
        var (weak, retainedView) = CreateRetainedView();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (weak.IsAlive || retainedView.GetPlainText() != "memory")
            throw new Exception("A retained view rooted the original event");
        GC.KeepAlive(retainedView);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, MessageEvent) CreateRetainedView()
    {
        var source = Message("!memory", true);
        return (new WeakReference(source), MessageTriggerPrefix.Strip(source, "!")!);
    }

    private static async Task VerifyConfigAsync(string root)
    {
        var path = Path.Combine(root, "config.toml");
        var manager = new ConfigManager(path);
        manager.SaveConfig(path, new CoreConfig());
        var schema =
            typeof(HostHttpServer).GetMethod("GetCoreConfigSchema", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, null);
        var schemaJson =
            JsonSerializer.SerializeToElement(schema, SchemaJsonOptions);
        var routeFields = schemaJson.EnumerateArray()
            .First(item => item.GetProperty("key").GetString() == "plugin_routes").GetProperty("fields");
        if (routeFields.EnumerateArray().All(item => item.GetProperty("key").GetString() != "message_prefix") ||
            routeFields.EnumerateArray().All(item => item.GetProperty("key").GetString() != "require_message_prefix"))
            throw new Exception("Host config schema hid the prefix settings");

        PatchCore("{\"plugin_routes\":{\"message_prefix\":\"!\",\"require_message_prefix\":true}}");
        var saved = await manager.LoadCoreConfig();
        if (saved.PluginRoutes.MessagePrefix != "!" || !saved.PluginRoutes.RequireMessagePrefix)
            throw new Exception("Host config API did not persist prefix settings");
        var savedText = await File.ReadAllTextAsync(path);
        foreach (var json in new[]
                 {
                     "{\"plugin_routes\":{\"message_prefix\":null}}",
                     "{\"plugin_routes\":{\"message_prefix\":\"partial\",\"require_message_prefix\":\"true\"}}"
                 })
        {
            try
            {
                PatchCore(json);
                throw new Exception("Invalid core prefix setting accepted");
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
            {
            }

            if (await File.ReadAllTextAsync(path) != savedText)
                throw new Exception("Invalid core patch partially changed config");
        }

        var routes = (await manager.LoadCoreConfig()).PluginRoutes;

        Patch("{\"prefix_mode\":\"disabled\"}");
        var loaded = (await manager.LoadCoreConfig()).PluginRoutes;
        if (routes.RequiresMessagePrefix("example") || loaded.RequiresMessagePrefix("EXAMPLE") ||
            loaded.Plugins.Count != 0 || !loaded.AllowsGroup("Example", "any"))
            throw new Exception("Prefix override changed group permissions or did not persist independently");
        Patch("{\"mode\":\"whitelist\",\"groups\":[\"allowed\"]}");
        if (routes.RequiresMessagePrefix("Example")) throw new Exception("Group edit erased prefix override");
        Patch("{\"prefix_mode\":\"default\"}");
        if (!routes.RequiresMessagePrefix("Example") ||
            (await manager.LoadCoreConfig()).PluginRoutes.PrefixModes.Count != 0)
            throw new Exception("Resetting prefix mode did not restore inheritance");
        var original = await File.ReadAllTextAsync(path);
        foreach (var json in new[]
                 {
                     "{\"prefix_mode\":\"invalid\",\"priority\":99}", "{\"prefix_mode\":true}", "{\"prefix_mode\":null}"
                 })
        {
            try
            {
                Patch(json);
                throw new Exception("Invalid prefix mode accepted");
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
            {
            }

            if (await File.ReadAllTextAsync(path) != original || routes.GetPriority("Example") is not null)
                throw new Exception("Invalid prefix patch partially changed config");
        }

        var response =
            typeof(HostHttpServer).GetMethod("CreatePluginRouteResponse", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [routes, "Example", 2]);
        var fields = JsonSerializer.SerializeToElement(response);
        if (fields.GetProperty("message_prefix").GetString() != "!" ||
            !fields.GetProperty("effective_require_prefix").GetBoolean())
            throw new Exception("Config API omitted effective prefix settings");
        Patch("{\"prefix_mode\":\"required\",\"custom_prefix\":\"bot:\"}");
        loaded = (await manager.LoadCoreConfig()).PluginRoutes;
        if (loaded.GetMessagePrefix("EXAMPLE") != "bot:" || routes.GetMessagePrefix("Example") != "bot:")
            throw new Exception("Plugin custom prefix did not persist independently");
        Patch("{\"prefix_mode\":\"default\"}");
        if (routes.GetMessagePrefix("Example") != "!")
            throw new Exception("Inherited mode used an inactive custom prefix");
        Patch("{\"prefix_mode\":\"required\",\"custom_prefix\":\"\"}");
        if (routes.GetMessagePrefix("Example") != "!" ||
            (await manager.LoadCoreConfig()).PluginRoutes.Prefixes.Count != 0)
            throw new Exception("Clearing a custom prefix did not restore global prefix");
        savedText = await File.ReadAllTextAsync(path);
        try
        {
            Patch("{\"custom_prefix\":null,\"priority\":99}");
            throw new Exception("Null custom prefix accepted");
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
        {
        }

        if (await File.ReadAllTextAsync(path) != savedText)
            throw new Exception("Invalid custom prefix patch changed config");

        return;

        void Patch(string json)
        {
            using var document = JsonDocument.Parse(json);
            typeof(HostHttpServer).GetMethod("ApplyPluginRoutePatch", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [manager, path, routes, "Example", document.RootElement]);
        }

        void PatchCore(string json)
        {
            using var document = JsonDocument.Parse(json);
            typeof(HostHttpServer).GetMethod("ApplyConfigPatch", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [document.RootElement, new ApiHostConfig { Token = "fixture" }, manager, path]);
        }
    }

    private static async Task VerifyDispatchAsync(string root)
    {
        var logs = new HostLogHub();
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var policy = new PluginRouteConfig
        {
            MessagePrefix = "!", RequireMessagePrefix = true,
            Default = new PluginRouteRuleConfig { Mode = "blacklist" },
            PrefixModes = { ["exempt"] = "disabled" }
        };
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var dispatcher =
            new HostEventDispatcher(new Lock(), bot.ReplySubscriptions, runtime, logs, routePolicy: policy);
        var received = new List<string>();
        await Register("required", 2);
        await Register("exempt", 2);
        await Register("command", 1, "#help");
        await Register("fallback", 10);
        dispatcher.MarkInitialPluginsReady();

        foreach (var direct in new[] { false, true })
        {
            Expect(await Send(Message("hello", direct)), "exempt:hello", "required:hello", "fallback:hello");
            Expect(await Send(Message("!hello", direct)), "exempt:!hello", "required:!hello", "fallback:!hello");
            var command = Message("! #help", direct);
            Expect(await Send(command), "command:#help");
            if (!command.IsHandled()) throw new Exception("Stripped command did not mark original as handled");
            Expect(await Send(Message("", direct) with { Segments = [new ImageSegment("file:///fixture.png")] }),
                "exempt:", "required:", "fallback:");
        }

        policy.Plugins["required"] = new PluginRouteRuleConfig { Mode = "whitelist", Groups = ["allowed"] };
        Expect(await Send(Message("!blocked", false)), "exempt:!blocked", "fallback:!blocked");
        policy.Plugins.Clear();
        policy.RequireMessagePrefix = false;
        policy.PrefixModes["required"] = "required";
        Expect(await Send(Message("plain", true)), "exempt:plain", "required:plain", "fallback:plain");
        Expect(await Send(Message("!again", true)), "exempt:!again", "required:!again", "fallback:!again");

        policy.Prefixes["required"] = "bot:";
        Expect(await Send(Message("bot:custom", true)), "exempt:bot:custom", "required:bot:custom",
            "fallback:bot:custom");
        Expect(await Send(Message("!global", true)), "exempt:!global", "required:!global", "fallback:!global");
        policy.MessagePrefix = "";
        Expect(await Send(Message("bot:standalone", false)), "exempt:bot:standalone", "required:bot:standalone",
            "fallback:bot:standalone");
        policy.MessagePrefix = "!";
        policy.RequireMessagePrefix = true;
        Expect(await Send(Message("!global", true)), "exempt:!global", "required:!global", "fallback:!global");
        Expect(await Send(Message("bot:local", true)), "exempt:bot:local", "required:bot:local", "fallback:bot:local");
        policy.Prefixes["command"] = "cmd:";
        policy.PrefixModes["command"] = "required";
        var customCommand = Message("cmd:#help", true);
        Expect(await Send(customCommand), "command:#help");
        if (!customCommand.IsHandled()) throw new Exception("Custom prefix command did not share handled mark");
        policy.Prefixes["command"] = "!!";
        Expect(await Send(Message("!!#help", true)), "command:#help");

        var updated = new PluginRouteConfig
        {
            MessagePrefix = "$", RequireMessagePrefix = true,
            Default = new PluginRouteRuleConfig { Mode = "blacklist" },
            PrefixModes = { ["EXEMPT"] = "disabled" }
        };
        policy.CopyFrom(updated);
        if (policy.Prefixes.Count != 0) throw new Exception("Hot config did not clear removed custom prefixes");
        Expect(await Send(Message("$hot", false)), "exempt:$hot", "required:$hot", "fallback:$hot");
        Expect(await Send(Message("!old", false)), "exempt:!old", "required:!old", "fallback:!old");
        var confirmation = false;
        using (bot.ReplySubscriptions.Subscribe("host",
                   new MessageReference("fixture", Channel.Direct("owner"), "notice"),
                   TimeSpan.FromMinutes(1), _ =>
                   {
                       confirmation = true;
                       return Task.CompletedTask;
                   }))
        {
            await Send(Message("确认", true) with { Segments = [new QuoteSegment("notice"), new TextSegment("确认")] });
        }

        if (!confirmation) throw new Exception("Prefix gate blocked an interactive reply subscription");
        lock (received)
        {
            received.Clear();
        }

        await dispatcher.PublishAsync(new MemberJoinedEvent
            { Platform = "fixture", Channel = Channel.Group("group"), UserId = "new" });
        lock (received)
        {
            Expect([.. received.Order(StringComparer.Ordinal)], "command:joined", "exempt:joined", "fallback:joined",
                "required:joined");
        }

        policy.MessagePrefix = "";
        Expect(await Send(Message("legacy", true)), "exempt:legacy", "required:legacy", "fallback:legacy");

        return;


        async Task<string[]> Send(MessageEvent message)
        {
            lock (received)
            {
                received.Clear();
            }

            await dispatcher.PublishAsync(message);
            lock (received)
            {
                return [.. received.Order(StringComparer.Ordinal)];
            }
        }

        async Task Register(string id, int priority, string? command = null)
        {
            var plugin = new RecordingPlugin(id, received, command);
            var context = new PluginContext(bot, id, Path.Combine(root, id), logs, new PluginServiceRegistry());
            await plugin.OnLoad(context);
            dispatcher.RegisterPlugin(new LoadedPluginHandle(plugin, context, new DllLoader<IBotPlugin>(),
                typeof(MessagePrefixVerification).Assembly.Location,
                new PluginProbeInfo(plugin.GetType().FullName!, id, id, "1.0.0", null, null, PluginCategory.Other, null,
                    false, [], [], priority),
                logs, group => policy.AllowsGroup(id, group)));
        }
    }

    private static async Task VerifyObserverRoutesAsync(string root)
    {
        var logs = new HostLogHub();
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var policy = new PluginRouteConfig { MessagePrefix = "!", RequireMessagePrefix = true };
        var dispatcher = new HostEventDispatcher(new Lock(), bot.ReplySubscriptions,
            new HostRuntimeState(DateTimeOffset.UtcNow), logs, routePolicy: policy);
        var received = new List<string>();
        foreach (var id in new[] { "all", "custom", "mention", "override", "events", "mixed", "command" })
        {
            var plugin = new ObserverPlugin(id, received);
            var context = new PluginContext(bot, id, Path.Combine(root, "observers", id), logs,
                new PluginServiceRegistry());
            await plugin.OnLoad(context);
            dispatcher.RegisterPlugin(new LoadedPluginHandle(plugin, context, new DllLoader<IBotPlugin>(),
                typeof(MessagePrefixVerification).Assembly.Location,
                new PluginProbeInfo(plugin.GetType().FullName!, id, id, "1.0.0", null, null, PluginCategory.Other, null,
                    false, [], []), logs));
        }

        dispatcher.MarkInitialPluginsReady();

        foreach (var direct in new[] { false, true })
        {
            await Send(Message("hello", direct), "all:hello", "custom:hello", "override:hello", "events:hello",
                "mixed:raw:hello");
            await Send(Message("!hello", direct), "all:!hello", "custom:!hello", "override:!hello", "events:!hello",
                "mixed:raw:!hello");
            await Send(Message("#help", direct), "all:#help", "custom:#help", "override:#help", "events:#help",
                "mixed:raw:#help");
            await Send(Message("!#help", direct), "all:!#help", "custom:!#help", "override:!#help", "events:!#help",
                "mixed:raw:!#help", "mixed:command:#help", "command:#help");
            await Send(
                Message("#help", direct) with { Segments = [new MentionSegment("bot"), new TextSegment("#help")] },
                "all:#help", "custom:#help", "override:#help", "events:#help", "mixed:raw:#help", "mixed:command:#help",
                "command:#help", "mention:#help");
            await Send(
                Message("!#help", direct) with { Segments = [new MentionSegment("bot"), new TextSegment("!#help")] },
                "all:!#help", "custom:!#help", "override:!#help", "events:!#help", "mixed:raw:!#help",
                "mixed:command:#help", "command:#help", "mention:!#help");
            // A MapMention callback receives any matching @ event, even when it mentions someone else.
            // Mentioning someone else does not bypass an ordinary command's prefix requirement.
            await Send(
                Message("#help", direct) with { Segments = [new MentionSegment("other"), new TextSegment("#help")] },
                "all:#help", "custom:#help", "override:#help", "events:#help", "mixed:raw:#help", "mention:#help");
            await Send(Message("", direct) with { Segments = [new ImageSegment("file:///fixture.png")] },
                "all:", "custom:", "override:", "events:", "mixed:raw:");
        }

        Console.WriteLine(
            "PASS @ routes and raw listeners bypass prefixes; mixed plugins still gate commands; @ bot bypasses commands; @ others does not.");

        return;

        async Task Send(MessageEvent message, params string[] expected)
        {
            lock (received)
            {
                received.Clear();
            }

            await dispatcher.PublishAsync(message);
            lock (received)
            {
                Expect([.. received.Order(StringComparer.Ordinal)], expected);
            }
        }
    }

    private static MessageEvent Message(string text, bool direct)
    {
        return new MessageEvent
        {
            Platform = "fixture", InstanceId = "fixture", SelfId = "bot", MessageId = Guid.NewGuid().ToString("N"),
            Channel = direct ? Channel.Direct("owner") : Channel.Group("group"), Sender = new User("owner"),
            Segments = [new TextSegment(text)]
        };
    }

    private static void Expect(string[] actual, params string[] expected)
    {
        if (!actual.SequenceEqual(expected.Order(StringComparer.Ordinal)))
            throw new Exception($"Received [{string.Join(',', actual)}], expected [{string.Join(',', expected)}]");
    }

    private sealed class ObserverPlugin(string id, List<string> received) : PluginBase
    {
        protected override void ConfigureRoutes()
        {
            Routes(GroupCommands);
            Routes(DirectCommands);
            if (id == "events") Events.Map<MessageEvent>(message => Record(id, message));

            return;

            void Routes(CommandRouter<MessageEvent> router)
            {
                switch (id)
                {
                    case "all": router.MapAll(message => Record(id, message)); break;
                    case "custom": router.MapWhen(_ => true, message => Record(id, message)); break;
                    case "mention": router.MapMention(message => Record(id, message)); break;
                    case "mixed" or "command":
                        router.MapExact("#help", message => Record(id == "mixed" ? "mixed:command" : id, message));
                        break;
                }
            }
        }

        protected override async Task OnGroupMessageAsync(MessageEvent message)
        {
            if (id is "override" or "mixed") await Record(id == "mixed" ? "mixed:raw" : id, message);
            await base.OnGroupMessageAsync(message);
        }

        protected override async Task OnDirectMessageAsync(MessageEvent message)
        {
            if (id is "override" or "mixed") await Record(id == "mixed" ? "mixed:raw" : id, message);
            await base.OnDirectMessageAsync(message);
        }

        private Task Record(string label, MessageEvent message)
        {
            lock (received)
            {
                received.Add(label + ":" + message.GetPlainText());
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlugin(string id, List<string> received, string? command) : PluginBase
    {
        protected override void ConfigureRoutes()
        {
            if (command is null)
            {
                GroupCommands.MapAll(Record);
                DirectCommands.MapAll(Record);
            }
            else
            {
                GroupCommands.MapExact(command, Record);
                DirectCommands.MapExact(command, Record);
            }

            Events.Map<MemberJoinedEvent>(_ =>
            {
                lock (received)
                {
                    received.Add(id + ":joined");
                }

                return Task.CompletedTask;
            });
        }

        private Task Record(MessageEvent message)
        {
            lock (received)
            {
                received.Add(id + ":" + message.GetPlainText());
            }

            return Task.CompletedTask;
        }
    }
}