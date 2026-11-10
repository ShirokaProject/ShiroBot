using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;

namespace ShiroBot.Verification;

internal static class HostHttpLifecycleVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shiro-http-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)occupied.LocalEndpoint).Port}";
        var config = new CoreConfig
        {
            Api = new ApiHostConfig
                { Enable = false, EnableDashboard = false, ListenUrls = [origin], Token = "original" }
        };
        var path = Path.Combine(root, "config.toml");
        var manager = new ConfigManager(path);
        manager.SaveConfig(path, config);
        var web = new WebHostContext(origin, true);
        var bot = new BotContext(null, [], [], web);
        using var logs = new HostLogHub();
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var shared = new SharedAssemblyResolver();
        var models = new ModelPackageRegistry(shared);
        var dispatcher = new HostEventDispatcher(new Lock(), bot.ReplySubscriptions, runtime, logs);
        var plugins = new PluginManager(bot, shared, models, runtime, logs)
            { PluginRootPath = Path.Combine(root, "plugins") };
        var adapters = new AdapterManager(Path.Combine(root, "adapters"), shared, models, bot,
            new AdapterEventBridge(dispatcher), runtime, logs, _ => Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(config.Api));
        services.AddSingleton(manager);
        services.AddSingleton(plugins);
        services.AddSingleton(dispatcher);
        services.AddSingleton(config.PluginRoutes);
        services.AddSingleton(web);
        services.AddSingleton(runtime);
        services.AddSingleton(logs);
        services.AddSingleton(bot);
        services.AddSingleton(models);
        services.AddSingleton(adapters);
        services.AddSingleton(new ComponentReloadCoordinator(adapters));
        services.AddSingleton(new AdapterPackageManager(Path.Combine(root, "adapters"), path));
        services.AddSingleton(new HostPowerControl(() => { }));
        services.AddHostHttpServer();
        try
        {
            await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions
                             { ValidateOnBuild = true, ValidateScopes = true }))
            {
                var server = provider.GetRequiredService<IHostedService>();
                try
                {
                    // Disabled API must not bind, even when its configured port is already occupied.
                    await server.StartAsync(CancellationToken.None);
                    await server.StopAsync(CancellationToken.None);
                    config.Api.Enable = true;
                    var bindFailed = false;
                    try
                    {
                        await server.StartAsync(CancellationToken.None);
                    }
                    catch (IOException)
                    {
                        bindFailed = true;
                    }

                    if (!bindFailed) throw new Exception("Occupied listener did not fail.");
                    occupied.Stop();
                    using (var canceled = new CancellationTokenSource())
                    {
                        await canceled.CancelAsync();
                        try
                        {
                            await server.StartAsync(canceled.Token);
                            throw new Exception("Canceled startup unexpectedly succeeded.");
                        }
                        catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
                    }
                    // A failed start must release the nested host and allow a clean retry.
                    await server.StartAsync(CancellationToken.None);
                    await server.StartAsync(CancellationToken.None);
                    // The isolated HTTP fixture owns this client for the complete listener lifetime.
                    // ReSharper disable once UseObjectOrCollectionInitializer, ShortLivedHttpClient
                    using var client = new HttpClient();
                    client.BaseAddress = new Uri(origin);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "original");
                    using var authenticated = await client.GetAsync("/api/v1/auth");
                    authenticated.EnsureSuccessStatusCode();
                    using (new CoreConfigWatcher(path, config, bot))
                    {
                        var updated = await manager.LoadCoreConfig();
                        updated.Api.Token = "rotated";
                        // Listener settings are saved/live-visible but still take effect only on restart.
                        updated.Api.Enable = false;
                        updated.Api.ListenUrls = ["http://127.0.0.1:0"];
                        manager.SaveConfig(path, updated);
                        var deadline = DateTime.UtcNow.AddSeconds(10);
                        while (config.Api.Token != "rotated" && DateTime.UtcNow < deadline) await Task.Delay(50);
                        if (config.Api.Token != "rotated")
                            throw new Exception("TOML watcher did not update live Options.");
                        using var rejected = await client.GetAsync("/api/v1/auth");
                        if (rejected.StatusCode != HttpStatusCode.Unauthorized)
                            throw new Exception("Old token remained valid.");
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "rotated");
                        using var accepted = await client.GetAsync("/api/v1/auth");
                        accepted.EnsureSuccessStatusCode();
                    }

                    // HTTP service shutdown must leave shared process services usable.
                    await server.StopAsync(CancellationToken.None);
                    await server.StopAsync(CancellationToken.None);
                }
                finally
                {
                    await server.StopAsync(CancellationToken.None);
                }
            }

            logs.Record("ownership-probe", "info", "still-owned-by-process");
            if (logs.GetHistory("ownership-probe", 1).All(entry => entry.Message != "still-owned-by-process"))
                throw new Exception("HTTP container disposed the process-owned log hub.");
            using var rebound = new TcpListener(IPAddress.Loopback, new Uri(origin).Port);
            rebound.Start();
        }
        finally
        {
            plugins.BeginShutdown();
            await adapters.StopForShutdownAsync();
            Directory.Delete(root, true);
        }

        Console.WriteLine(
            "HTTP DI lifecycle, disabled listener, startup failure recovery and live TOML token rotation verification passed.");
    }
}