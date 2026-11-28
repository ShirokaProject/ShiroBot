using System.Runtime.CompilerServices;
using System.Reflection;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Components.Updates;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Commands;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.Terminal;
using ProbePlugin = ShiroBot.SharedContractPluginProbe.SharedContractPluginProbe;

namespace ShiroBot.Verification;

internal static class AdapterActionsVerification
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.AdapterActions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Probe.dll");
        File.Copy(typeof(ProbePlugin).Assembly.Location, path);
        var shared = new SharedAssemblyResolver();
        var models = new ModelPackageRegistry(shared);
        var logs = new HostLogHub();
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var dispatcher = new HostEventDispatcher(new Lock(), context.ReplySubscriptions, runtime, logs);
        var adapters = new AdapterManager(root, shared, models, context, new AdapterEventBridge(dispatcher),
            runtime, logs);
        var configPath = Path.Combine(root, "config.toml");
        AdapterInstanceStore.Write(configPath, [new PackageAdapterInstance { Id = "a", Enabled = true }], true);
        var instance = new InstalledAdapterInstance("a", "lifecycle-probe", path, configPath, true, "Actions");
        try
        {
            await adapters.LoadInstanceAsync(instance);
            var actions = await adapters.GetActionsAsync("a");
            if (actions.Count != 1 || actions[0].Id != "ping") throw new Exception("Adapter actions were not filtered.");
            var commands = new HostCommandHandler(null!, null!, null!, null!, null!, adapters, null!,
                new AdapterPackageManager(root), runtime, null!);
            var completionMethod = typeof(HostCommandHandler).GetMethod("BuildAdapterCompletions",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            ConsoleOutput.ConsoleCommandOption[] Completions() =>
                ((IEnumerable<ConsoleOutput.ConsoleCommandOption>)completionMethod.Invoke(commands, null)!).ToArray();
            var completions = Completions();
            foreach (var name in new[] { "adapter actions", "adapter action", "adapter actions a", "adapter action a", "adapter action a ping" })
                if (!completions.Any(option => option.Name == name))
                    throw new Exception($"Missing adapter completion: {name}");
            if (completions.Any(option => option.Name.StartsWith("adapter action a ") && option.Name != "adapter action a ping"))
                throw new Exception("Invalid or duplicate adapter actions leaked into completions.");
            try { await adapters.ExecuteActionAsync("a", "missing"); throw new Exception("Unknown action accepted."); }
            catch (KeyNotFoundException) { }
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { await adapters.ExecuteActionAsync("a", "ping", canceled.Token); throw new Exception("Canceled action executed."); }
            catch (OperationCanceledException) { }
            var pending = adapters.ExecuteActionAsync("a", "ping");
            var stop = adapters.StopByIdAsync("a");
            var result = await pending;
            if (!result.Ok || result.Message != "ping") throw new Exception("Adapter action result was lost.");
            await stop;
            if (Completions().Any(option => option.Name.StartsWith("adapter action a") || option.Name == "adapter actions a"))
                throw new Exception("Stopped adapter remained in action completions.");
            try { await adapters.GetActionsAsync("a"); throw new Exception("Stopped adapter remained callable."); }
            catch (KeyNotFoundException) { }
            Console.WriteLine("Adapter actions: filtering, dispatch, cancellation and stop serialization passed.");
        }
        finally
        {
            await adapters.StopByIdAsync("a");
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
