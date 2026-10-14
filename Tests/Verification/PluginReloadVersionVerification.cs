using System.Reflection;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Update;

internal static class PluginReloadVersionVerification
{
    internal static async Task RunAsync(string fixtureDirectory)
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.PluginReloadVersion", Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(root, "plugins");
        var directory = Path.Combine(pluginRoot, "update-probe.plugin");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ShiroBot.UpdateProbe.dll");
        var shared = new SharedAssemblyResolver();
        var logs = new HostLogHub();
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var dispatcher = new HostEventDispatcher(new Lock(), context.ReplySubscriptions, runtime, logs);
        var plugins = new PluginManager(context, shared, new ModelPackageRegistry(shared), runtime, logs) { PluginRootPath = pluginRoot };
        Assembly? oldAssembly = null;
        try
        {
            File.Copy(Path.Combine(fixtureDirectory, "v1", "ShiroBot.UpdateProbe.dll"), path);
            await plugins.LoadPluginsAsync([path], dispatcher, new PluginRouteConfig());
            AssertVersion("1.0.0");
            oldAssembly = plugins.GetLoadedPluginSnapshot().Single().PluginAssembly!;
            // Intentionally pin v1's assembly to reproduce an incomplete collectible unload.
            var package = PluginUpdateService.PreparePluginUploadPackage(plugins,
                Path.Combine(fixtureDirectory, "v2", "ShiroBot.UpdateProbe.dll"));
            var loaded = plugins.GetLoadedPluginSnapshot().Single();
            var result = await PluginUpdateService.ReplaceInstalledPluginAsync(plugins, dispatcher, new PluginRouteConfig(), package,
                new PluginUpdateService.InstalledPluginInfo(path, "1.0.0"), loaded);
            if (result.PendingReason is not null) throw new Exception("Shadow-copied v1 prevented replacement: " + result.PendingReason);
            await plugins.ScheduleLoadPluginByName(dispatcher, new PluginRouteConfig(), "update-probe.plugin");
            AssertVersion("2.0.0");
            if (ReferenceEquals(oldAssembly, plugins.GetLoadedPluginSnapshot().Single().PluginAssembly))
                throw new Exception("Update reused the resident v1 assembly.");
            GC.KeepAlive(oldAssembly);
            oldAssembly = plugins.GetLoadedPluginSnapshot().Single().PluginAssembly;
            // Manual deployment can truncate the installed file before requesting a reload.
            File.Copy(Path.Combine(fixtureDirectory, "v1", "ShiroBot.UpdateProbe.dll"), path, overwrite: true);
            await plugins.UnloadAllAsync(dispatcher);
            await plugins.ScheduleLoadPluginByName(dispatcher, new PluginRouteConfig(), "update-probe.plugin");
            AssertVersion("1.0.0");
            GC.KeepAlive(oldAssembly);
            oldAssembly = null;
            await plugins.UnloadAllAsync(dispatcher);
            await plugins.ScheduleLoadPluginByName(dispatcher, new PluginRouteConfig(), "update-probe.plugin");
            AssertVersion("1.0.0");
            Console.WriteLine("PASS: PluginPackageReplacementManualOverwriteAndReloadExecuteNewVersionWithOldAssemblyResident");
        }
        finally
        {
            oldAssembly = null;
            await plugins.UnloadAllAsync(dispatcher);
            Directory.Delete(root, recursive: true);
        }

        void AssertVersion(string expected)
        {
            var loaded = plugins.GetLoadedPluginSnapshot().Single();
            var executed = File.ReadAllText(Path.Combine(directory, "running-version.txt"));
            if (loaded.Version != expected || executed != expected)
                throw new Exception($"Expected {expected}, metadata={loaded.Version}, executing={executed}.");
        }
    }
}
