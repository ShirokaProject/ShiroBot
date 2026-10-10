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
using ShiroBot.SharedContractPluginProbe;

internal static class AdapterReloadIsolationVerification
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.ReloadIsolation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Probe.dll");
        File.Copy(typeof(SharedContractPluginProbe).Assembly.Location, path);
        var pluginRoot = Path.Combine(root, "plugins");
        var pluginDirectory = Path.Combine(pluginRoot, "Probe");
        Directory.CreateDirectory(pluginDirectory);
        var pluginPath = Path.Combine(pluginDirectory, "Plugin.dll");
        File.Copy(path, pluginPath);
        var shared = new SharedAssemblyResolver();
        var models = new ModelPackageRegistry(shared);
        var logs = new HostLogHub();
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var dispatcher = new HostEventDispatcher(new Lock(), context.ReplySubscriptions, runtime, logs);
        var adapters = new AdapterManager(root, shared, models, context, new AdapterEventBridge(dispatcher),
            runtime, logs, _ => Task.CompletedTask);
        var plugins = new PluginManager(context, shared, models, runtime, logs) { PluginRootPath = pluginRoot };
        var coordinator = new ComponentReloadCoordinator(adapters);
        var configPath = Path.Combine(root, "config.toml");
        AdapterInstanceStore.Write(configPath, [new PackageAdapterInstance { Id = "a", Enabled = true },
            new PackageAdapterInstance { Id = "b", Enabled = true }], replaceLegacy: true);
        var first = new InstalledAdapterInstance("a", "lifecycle-probe", path, configPath, true, "First");
        var second = new InstalledAdapterInstance("b", "lifecycle-probe", path, configPath, true, "Second");
        try
        {
            await plugins.LoadPluginsAsync([pluginPath], dispatcher, new PluginRouteConfig
            {
                Default = new PluginRouteRuleConfig { Mode = "blacklist" }
            });
            var handle = plugins.GetLoadedPluginSnapshot().Single();
            dispatcher.MarkInitialPluginsReady();
            await coordinator.ExecuteAdapterStartAsync(() => adapters.LoadInstanceAsync(first));
            await coordinator.ExecuteAdapterStartAsync(() => adapters.LoadInstanceAsync(second));
            var secondAssembly = adapters.GetLoadedAssembly("b");
            await coordinator.ReloadAdapterByIdAsync("a");
            await AssertPlugin(1);
            if (!ReferenceEquals(secondAssembly, adapters.GetLoadedAssembly("b")))
                throw new InvalidOperationException("Reloading a replaced another adapter instance.");
            await coordinator.ExecuteAdapterMutationAsync(() => adapters.StopByIdAsync("a"));
            await coordinator.ExecuteAdapterStartAsync(() => adapters.LoadInstanceAsync(first));
            var result = await coordinator.ExecuteAdapterMutationAsync(async () =>
            {
                await adapters.ReloadByIdAsync("a");
                return 42;
            });
            if (result != 42) throw new InvalidOperationException("Adapter mutation result lost.");
            await AssertPlugin(2);

            using (context.UseInstance("a"))
                await CallPlugin("CacheAdapter");
            await handle.DispatchAsync<IBotPlugin>(plugin =>
            {
                if (!(bool)plugin.GetType().GetProperty("HasCachedAdapter")!.GetValue(plugin)!)
                    throw new InvalidOperationException("Probe did not retain an adapter extension.");
                return Task.CompletedTask;
            });
            try
            {
                await coordinator.ReloadAdapterByIdAsync("a");
                throw new InvalidOperationException("Referenced old adapter assembly was replaced.");
            }
            catch (ComponentUnloadPendingException) { }
            if (adapters.LoadedIds.Contains("a") || !adapters.GetSnapshot().Any(item => item.Id == "a" && item.RestartRequired))
                throw new InvalidOperationException("Unload failure was not marked as requiring a restart.");
            try
            {
                await coordinator.ExecuteAdapterStartAsync(() => adapters.LoadInstanceAsync(first));
                throw new InvalidOperationException("Pending adapter instance was started again.");
            }
            catch (ComponentUnloadPendingException) { }
            await AssertPlugin(3);
            if (!ReferenceEquals(secondAssembly, adapters.GetLoadedAssembly("b")))
                throw new InvalidOperationException("Unload failure disrupted another instance.");
            // Failure must release the coordinator gate and keep the remaining adapter operable.
            await CallPlugin("ClearAdapter");
            secondAssembly = null;
            await coordinator.ReloadAdapterByIdAsync("b");
            await AssertPlugin(4);
            Console.WriteLine("Adapter reload/stop and unload failure preserve plugin identity, lifecycle, subscriptions and independent instances verification passed.");

            async Task CallPlugin(string method) => await handle.DispatchAsync<IBotPlugin>(plugin =>
            {
                plugin.GetType().GetMethod(method)!.Invoke(plugin, null);
                return Task.CompletedTask;
            });
            async Task AssertPlugin(int events)
            {
                if (!ReferenceEquals(handle, plugins.GetLoadedPluginSnapshot().Single()))
                    throw new InvalidOperationException("Adapter operation replaced a plugin handle.");
                await dispatcher.PublishAsync(new MemberJoinedEvent { Platform = "lifecycle-probe", InstanceId = "b",
                    Channel = Channel.Group("group"), UserId = "user" });
                var dispatched = await handle.DispatchAsync<IBotPlugin>(plugin =>
                {
                    int Read(string name) => (int)plugin.GetType().GetProperty(name)!.GetValue(plugin)!;
                    if (Read("Loads") != 1 || Read("Unloads") != 0 || Read("EventsReceived") != events)
                        throw new InvalidOperationException($"Adapter operation restarted a plugin or lost/duplicated its subscription: loads={Read("Loads")}, unloads={Read("Unloads")}, events={Read("EventsReceived")}, expected={events}.");
                    return Task.CompletedTask;
                });
                if (!dispatched) throw new InvalidOperationException("Plugin no longer accepts dispatch.");
            }
        }
        finally
        {
            await plugins.UnloadAllAsync(dispatcher);
            await adapters.StopForShutdownAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
