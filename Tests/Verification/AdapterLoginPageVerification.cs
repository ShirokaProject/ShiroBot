using System.Runtime.CompilerServices;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Components.Updates;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ProbePlugin = ShiroBot.SharedContractPluginProbe.SharedContractPluginProbe;

namespace ShiroBot.Verification;

internal static class AdapterLoginPageVerification
{
    internal static async Task RunAsync(string adapterPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.AdapterActions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Probe.dll");
        File.Copy(adapterPath, path);
        var shared = new SharedAssemblyResolver();
        var models = new ModelPackageRegistry(shared);
        models.RegisterBuiltIn(typeof(ShiroBot.Model.QQ.QFriend).Assembly);
        var logs = new HostLogHub();
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var dispatcher = new HostEventDispatcher(new Lock(), context.ReplySubscriptions, runtime, logs);
        var adapters = new AdapterManager(root, shared, models, context, new AdapterEventBridge(dispatcher),
            runtime, logs);
        var configPath = Path.Combine(root, "config.toml");
        AdapterInstanceStore.Write(configPath, [
            new PackageAdapterInstance { Id = "a", Enabled = true, Config = new() { ["uin"] = 123L, ["auto_login"] = false } },
            new PackageAdapterInstance { Id = "b", Enabled = true, Config = new() { ["uin"] = "456", ["auto_login"] = false } }
        ], true);
        var instance = new InstalledAdapterInstance("a", "lagrange", path, configPath, true, "Actions");
        try
        {
            await adapters.LoadInstanceAsync(instance);
            await adapters.LoadInstanceAsync(new InstalledAdapterInstance("b", "lagrange", path, configPath, true, "Second"));
            var storageA = (string)AdapterInstanceStore.GetConfig(configPath, "a")["keystore_path"]!;
            var storageB = (string)AdapterInstanceStore.GetConfig(configPath, "b")["keystore_path"]!;
            if (storageA == storageB) throw new Exception("Host instances share session storage.");
            if (!(await adapters.GetActionsAsync("a")).Any(x => x.Id == "quick-login")) throw new Exception("Quick action not exposed.");
            if ((await adapters.ExecuteActionAsync("a", "quick-login")).Ok) throw new Exception("Missing session login unexpectedly succeeded.");
            var ownerB = adapters.GetDashboardOwner("b")!;
            var stateB = await ShiroBot.Plugins.PluginDashboard.WebCallAsync(ownerB, "login", "status", null, CancellationToken.None);
            if (stateB.Value?["status"]?.GetValue<string>() != "ready") throw new Exception("Instance B state was changed by instance A.");
            Console.WriteLine("Two host instances: separate persisted keystore paths, quick-login dispatch and independent page state passed.");
            if (AdapterInstanceStore.GetConfig(configPath, "a")["uin"] is not string account || account != "123")
                throw new Exception("Legacy numeric QQ account was not migrated to a string.");
            var owner = adapters.GetDashboardOwner("a") ?? throw new Exception("Adapter dashboard owner missing.");
            AdapterInstanceStore.Update(configPath, "a", item => item.Config["session_store"] = "{\"Uin\":123,\"DeviceName\":\"stored-device\",\"WLoginSigs\":{}}");
            await Task.Delay(1200);
            if (!ReferenceEquals(owner, adapters.GetDashboardOwner("a"))) throw new Exception("Session refresh restarted the adapter.");
            if (!AdapterInstanceStore.GetConfig(configPath, "b").ContainsKey("session_store"))
                throw new Exception("Instance B session field was lost.");
            Console.WriteLine("Inline session field updates preserve instance isolation without restarting adapters.");
            var page = ShiroBot.Plugins.PluginDashboard.List(owner).Single();
            if (page.Id != "login" || page.Web is null) throw new Exception("Login web page not registered.");
            var html = await ShiroBot.Plugins.PluginDashboard.WebDocumentAsync(owner, "login", CancellationToken.None);
            if (html.Status != ShiroBot.Plugins.PluginPageStatus.Done || !html.Value!.Contains("账号密码登录"))
                throw new Exception("Embedded login page could not be read.");
            page = null!;
            var state = await ShiroBot.Plugins.PluginDashboard.WebCallAsync(owner, "login", "status", null, CancellationToken.None);
            if (state.Status != ShiroBot.Plugins.PluginPageStatus.Done || state.Value?["status"]?.GetValue<string>() != "ready")
                throw new Exception("Login page status call failed.");
            var invalid = await ShiroBot.Plugins.PluginDashboard.WebCallAsync(owner, "login", "password-login",
                new System.Text.Json.Nodes.JsonObject { ["uin"] = "invalid", ["password"] = "test-secret" }, CancellationToken.None);
            if (invalid.Status == ShiroBot.Plugins.PluginPageStatus.Done || File.ReadAllText(configPath).Contains("test-secret"))
                throw new Exception("Invalid credentials accepted or password persisted.");
            try { await adapters.StopByIdAsync("a"); }
            catch (ComponentUnloadPendingException) { Console.WriteLine("Adapter stopped; full assembly collection still requires host restart."); }
            if (adapters.GetDashboardOwner("a") is not null) throw new Exception("Stopped dashboard still registered.");
            var stopped = await ShiroBot.Plugins.PluginDashboard.WebCallAsync(owner, "login", "status", null, CancellationToken.None);
            if (stopped.Status == ShiroBot.Plugins.PluginPageStatus.Done) throw new Exception("Stopped page remained callable.");
            Console.WriteLine("Adapter login page: registration, embedded HTML, bridge status, input validation and lifetime checks passed.");
        }
        finally
        {
            foreach (var id in new[] { "a", "b" })
                try { await adapters.StopByIdAsync(id); } catch (ComponentUnloadPendingException) { }
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
