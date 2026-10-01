using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Components.Updates;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Commands;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.SDK.Plugin;
using ShiroBot.Update;

internal static class UpdateIntegration
{
    private const string PluginId = "update-probe.plugin";
    private const string AdapterId = "update-probe.adapter";
    private static Assembly? _pinnedAdapter;

    public static async Task RunAsync(string fixtureDirectory)
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.UpdateIntegration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var report = new List<object>();
        var packages = Path.Combine(root, "packages");
        Directory.CreateDirectory(packages);
        foreach (var version in new[] { 1, 2 })
        {
            MakePackage(Path.Combine(fixtureDirectory, $"v{version}", "ShiroBot.UpdateProbe.dll"),
                Path.Combine(packages, $"v{version}.zip"), version);
        }
        MakePackage(Path.Combine(fixtureDirectory, "v2", "ShiroBot.UpdateProbe.dll"), Path.Combine(packages, "broken.zip"), 2, broken: true);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var releases = builder.Build();
        var releaseOrigin = $"http://127.0.0.1:{FreePort()}";
        releases.Urls.Add(releaseOrigin);
        var releasePackage = "v2.zip";
        releases.MapGet("/github/repos/update-integration/{component}/releases/latest", (string component) => Results.Ok(new
        {
            tag_name = "v2.0.0", name = "Integration v2", html_url = releaseOrigin + "/release/v2", body = "Controlled update fixture",
            assets = new[] { new { name = releasePackage, browser_download_url = releaseOrigin + "/packages/" + releasePackage } }
        }));
        releases.MapGet("/packages/{name}", (string name) => Results.File(Path.Combine(packages, Path.GetFileName(name)), "application/zip"));
        await releases.StartAsync();
        var originalHttp = Updater.HttpClient;
        using var releaseHttp = new HttpClient(new ReleaseRedirectHandler(releaseOrigin));
        Updater.HttpClient = releaseHttp;
        Updater.Initialize(() => [], (_, _) => Task.CompletedTask);
        Console.WriteLine($"Integration artifacts: {root}");
        try
        {
            await Scenario("plugin-dll-upload", async host =>
            {
                await host.InstallOldAsync("plugins", Path.Combine(fixtureDirectory, "v1", "ShiroBot.UpdateProbe.dll"));
                host.AssertPluginVersion("1.0.0");
                return new { dll_uploaded = true, preview_confirmed = true, running_version = "1.0.0" };
            });
            await Scenario("adapter-dll-upload", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(fixtureDirectory, "v1", "ShiroBot.UpdateProbe.dll"));
                host.AssertAdapterVersion("1.0.0");
                return new { dll_uploaded = true, preview_confirmed = true, running_version = "1.0.0" };
            });
            await Scenario("same-platform-adapters", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(fixtureDirectory, "v1", "ShiroBot.UpdateProbe.dll"));
                await host.InstallOldAsync("adapters", Path.Combine(fixtureDirectory, "second", "ShiroBot.UpdateProbe.dll"));
                var loaded = host.Adapters.GetSnapshot();
                Check(loaded.Count == 2 && loaded.All(adapter => adapter.Loaded && adapter.Platform == "update-probe"),
                    "Two different adapter IDs on the same platform did not load simultaneously");
                await host.Adapters.StopByIdAsync("update-probe.second-adapter");
                Check(host.Adapters.LoadedIds.SequenceEqual([AdapterId]), "Stopping one adapter affected the other instance");
                return new { same_platform_loaded = true, separate_ids = true, independent_stop = true };
            });
            await Scenario("plugin-http", async host =>
            {
                await host.InstallOldAsync("plugins", Path.Combine(packages, "v1.zip"));
                host.AssertPluginVersion("1.0.0");
                host.SeedUserFiles("plugins", PluginId);
                var response = await host.PostAsync($"/plugins/{PluginId}/update", new { });
                Check(!response.TryGetProperty("pending_restart", out var pending) || !pending.GetBoolean(), "Normal plugin update unexpectedly staged");
                host.AssertPluginVersion("2.0.0");
                host.AssertPackage("plugins", PluginId);
                var latest = await host.PostAsync($"/plugins/{PluginId}/update", new { });
                Check(latest.GetProperty("message").GetString()!.Contains("最新版本"), "Second plugin update was not a no-op");
                return new { from = "1.0.0", to = "2.0.0", hot_replaced = true, config_and_data_preserved = true, already_latest = true };
            });
            await Scenario("plugin-console", async host =>
            {
                await host.InstallOldAsync("plugins", Path.Combine(packages, "v1.zip"));
                host.SeedUserFiles("plugins", PluginId);
                var check = await host.ConsoleUpdateAsync("update", "check", "plugins");
                var pending = Updater.GetPendingUpdates().Single(item => item.Name == PluginId);
                Check(check.Contains("1.0.0 -> 2.0.0"), "Console check did not find v2");
                var confirm = await host.ConsoleUpdateAsync("update", "confirm", pending.Id);
                Check(!confirm.Contains("失败"), "Console confirm failed: " + confirm);
                host.AssertPluginVersion("2.0.0");
                host.AssertPackage("plugins", PluginId);
                return new { from = "1.0.0", to = "2.0.0", checked_and_confirmed = true, config_and_data_preserved = true };
            });
            await Scenario("plugin-staged", async host =>
            {
                await host.InstallOldAsync("plugins", Path.Combine(packages, "v1.zip"));
                host.SeedUserFiles("plugins", PluginId);
                var blocker = Path.Combine(host.Root, "plugins", PluginId, "hold-unload");
                File.WriteAllText(blocker, "hold");
                var response = await host.PostAsync($"/plugins/{PluginId}/update", new { });
                Check(response.GetProperty("pending_restart").GetBoolean(), "Unload failure did not stage the plugin");
                host.AssertPluginVersion("1.0.0");
                Check(StagedComponentUpdates.HasStaged(Path.Combine(host.Root, "plugins"), PluginId), "Missing staged plugin");
                File.Delete(blocker);
                await host.Plugins.UnloadAllAsync(host.Dispatcher);
                var applied = StagedComponentUpdates.ApplyStaged(Path.Combine(host.Root, "plugins"));
                Check(applied.Applied.Contains(PluginId) && applied.Failed.Count == 0, "Startup could not apply staged plugin");
                await host.Plugins.ScheduleLoadPluginByName(host.Dispatcher, host.Policy, PluginId);
                host.AssertPluginVersion("2.0.0");
                host.AssertPackage("plugins", PluginId);
                return new { pending_restart = true, old_version_kept = true, startup_version = "2.0.0" };
            });
            await Scenario("plugin-delete-staged-update", async host =>
            {
                await host.InstallOldAsync("plugins", Path.Combine(packages, "v1.zip"));
                var pluginRoot = Path.Combine(host.Root, "plugins");
                var pluginFolder = Path.Combine(pluginRoot, PluginId);
                var blocker = Path.Combine(pluginFolder, "hold-unload");
                File.WriteAllText(blocker, "hold");
                var staged = await host.PostAsync($"/plugins/{PluginId}/update", new { });
                Check(staged.GetProperty("pending_restart").GetBoolean(), "Expected a staged update before deletion");
                File.Delete(blocker);
                var deleted = await host.PostAsync($"/plugins/{PluginId}/delete", new { });
                Check(!deleted.TryGetProperty("pending_restart", out var pending) || !pending.GetBoolean(), "An available plugin was not immediately deleted");
                Check(!Directory.Exists(pluginFolder) && !StagedComponentUpdates.HasStaged(pluginRoot, PluginId), "Deleted plugin or old staged update still exists");
                StagedComponentUpdates.ApplyStagedDeletions(pluginRoot);
                StagedComponentUpdates.ApplyStaged(pluginRoot);
                Check(!Directory.Exists(pluginFolder), "Startup resurrected the deleted plugin");
                return new { immediately_deleted = true, staged_update_cleared = true, not_reinstalled_at_startup = true };
            });
            await Scenario("plugin-pending-delete", async host =>
            {
                await host.InstallOldAsync("plugins", Path.Combine(packages, "v1.zip"));
                var pluginRoot = Path.Combine(host.Root, "plugins");
                var pluginFolder = Path.Combine(pluginRoot, PluginId);
                File.WriteAllText(Path.Combine(pluginFolder, "hold-unload"), "hold");
                await host.PostAsync($"/plugins/{PluginId}/update", new { });
                var requestId = await Updater.RequestPluginUpdateAsync(new PluginUpdateRequest(PluginId, "1.0.0", "2.0.0"));
                var deleted = await host.PostAsync($"/plugins/{PluginId}/delete", new { });
                Check(deleted.GetProperty("pending_delete").GetBoolean() && deleted.GetProperty("pending_restart").GetBoolean(), "Failed unload did not return deferred deletion");
                Check(Directory.Exists(pluginFolder) && StagedComponentUpdates.HasStagedDeletion(pluginRoot, PluginId), "Deferred deletion was not persisted");
                Check(!Updater.GetPendingUpdates().Any(request => request.Id == requestId), "Deleting a plugin left its console update request pending");
                var updates = StagedComponentUpdates.ApplyStaged(pluginRoot);
                Check(updates.Applied.Count == 0 && updates.Failed.Count == 0, "An update ran over a pending deletion");
                // A reinstall cannot replace the deletion marker with a fresh package.
                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(packages, "v2.zip"))), "file", "v2.zip");
                    using var uploadResponse = await host.Client.PostAsync("plugins/upload", content);
                    var preview = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync()).RootElement.Clone();
                    await host.PostAsync($"/plugins/upload/{preview.GetProperty("upload_id").GetString()}/confirm", new { replace = true, enable = true }, expectFailure: true);
                }
                var applied = StagedComponentUpdates.ApplyStagedDeletions(pluginRoot);
                Check(applied.Applied.Contains(PluginId) && applied.Failed.Count == 0, "Startup did not complete deferred deletion");
                Check(!Directory.Exists(pluginFolder) && !StagedComponentUpdates.HasStaged(pluginRoot, PluginId), "Deferred deletion left installed or staged files");
                StagedComponentUpdates.ApplyStaged(pluginRoot);
                Check(!Directory.Exists(pluginFolder), "Pending deletion resurrected a staged update");
                return new { pending_delete = true, old_update_cancelled = true, reinstall_blocked = true, deleted_at_startup = true };
            });
            await Scenario("adapter-http", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(packages, "v1.zip"));
                host.AssertAdapterVersion("1.0.0");
                host.SeedUserFiles("adapters", AdapterId);
                var response = await host.UpdateAdapterAsync();
                Check(!response.TryGetProperty("pending_restart", out var pending) || !pending.GetBoolean(), "Normal adapter update unexpectedly staged");
                host.AssertAdapterVersion("2.0.0");
                host.AssertPackage("adapters", AdapterId);
                return new { from = "1.0.0", to = "2.0.0", hot_replaced = true, config_and_data_preserved = true };
            });
            await Scenario("adapter-stop-restart-prompt", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(packages, "v1.zip"));
                _pinnedAdapter = host.Adapters.GetLoadedAssembly(AdapterId);
                JsonElement response;
                try { response = await host.PostAsync($"/adapters/{AdapterId}/stop", new { }, expectFailure: true); }
                finally { _pinnedAdapter = null; }
                Check(!response.GetProperty("ok").GetBoolean() && response.GetProperty("restartRequired").GetBoolean(),
                    "A stopped adapter with a pinned assembly did not request the restart dialog");
                return new { stopped = true, restart_dialog_required = true };
            });
            await Scenario("adapter-delete-stale-state", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(packages, "v1.zip"));
                _pinnedAdapter = host.Adapters.GetLoadedAssembly(AdapterId);
                try { await host.PostAsync($"/adapters/{AdapterId}/stop", new { }, expectFailure: true); }
                finally { _pinnedAdapter = null; }
                Check(host.Adapters.GetSnapshot().Any(item => item.Id == AdapterId && item.Error is not null && item.RestartRequired), "Missing stale pending-restart adapter error fixture");
                // Reproduce a package already deleted while the unload error remains.
                host.AdapterPackages.Uninstall(AdapterId);
                using var deleted = await host.Client.DeleteAsync($"adapters/{AdapterId}");
                Check(deleted.IsSuccessStatusCode, "Deleting an already absent package failed");
                Check(!host.Adapters.GetSnapshot().Any(item => item.Id == AdapterId), "Deleted adapter remained as an error-only ghost");
                using var repeated = await host.Client.DeleteAsync($"adapters/{AdapterId}");
                Check(repeated.IsSuccessStatusCode, "Repeated adapter deletion was not idempotent");
                return new { absent_package_deleted = true, stale_error_cleared = true, repeated_delete_ok = true };
            });
            await Scenario("adapter-delete-pinned", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(packages, "v1.zip"));
                _pinnedAdapter = host.Adapters.GetLoadedAssembly(AdapterId);
                try
                {
                    using var deleted = await host.Client.DeleteAsync($"adapters/{AdapterId}");
                    Check(deleted.IsSuccessStatusCode, "Pinned assembly blocked deleting adapter files");
                    using var response = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync());
                    Check(response.RootElement.GetProperty("restartRequired").GetBoolean(), "Deleted pinned adapter did not offer restart");
                    Check(host.AdapterPackages.Get(AdapterId) is null && !host.Adapters.GetSnapshot().Any(item => item.Id == AdapterId),
                        "Deleted pinned adapter left installed files or ghost state");
                }
                finally { _pinnedAdapter = null; }
                return new { files_deleted = true, ghost_removed = true, restart_required = true };
            });
            await Scenario("adapter-staged", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(packages, "v1.zip"));
                host.SeedUserFiles("adapters", AdapterId);
                _pinnedAdapter = host.Adapters.GetLoadedAssembly(AdapterId);
                JsonElement response;
                try { response = await host.UpdateAdapterAsync(); }
                finally { _pinnedAdapter = null; }
                Check(response.GetProperty("pending_restart").GetBoolean(), "Pinned adapter assembly did not cause staging");
                host.AssertAdapterVersion("1.0.0");
                Check(host.AdapterPackages.HasStagedUpdate(AdapterId), "Missing staged adapter");
                await host.Adapters.StopAsync();
                var applied = host.AdapterPackages.ApplyStagedUpdates();
                Check(applied.Applied.Contains(AdapterId) && applied.Failed.Count == 0, "Startup could not apply staged adapter");
                var package = host.AdapterPackages.Get(AdapterId)!;
                await host.Adapters.LoadByIdAsync(AdapterId, package.AssemblyPath);
                host.AssertAdapterVersion("2.0.0");
                host.AssertPackage("adapters", AdapterId);
                return new { pending_restart = true, old_version_kept = true, startup_version = "2.0.0" };
            });
            await Scenario("adapter-shutdown", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(packages, "v1.zip"));
                _pinnedAdapter = host.Adapters.GetLoadedAssembly(AdapterId);
                try
                {
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    await host.Adapters.StopForShutdownAsync();
                    Check(elapsed.Elapsed < TimeSpan.FromSeconds(3), "Process shutdown waited for a pinned adapter assembly");
                    Check(host.Adapters.LoadedIds.Count == 0, "Process shutdown left an adapter registered");
                    GC.KeepAlive(_pinnedAdapter);
                    return new { assembly_still_pinned = true, stopped_without_collection_wait = true, elapsed_ms = elapsed.ElapsedMilliseconds };
                }
                finally { _pinnedAdapter = null; }
            });
            releasePackage = "broken.zip";
            await Scenario("adapter-rollback", async host =>
            {
                await host.InstallOldAsync("adapters", Path.Combine(packages, "v1.zip"));
                host.SeedUserFiles("adapters", AdapterId);
                var response = await host.UpdateAdapterAsync(expectFailure: true);
                Check(response.GetProperty("rollback").GetBoolean(), "Failed startup did not report rollback");
                host.AssertAdapterVersion("1.0.0");
                Check(host.AdapterPackages.Get(AdapterId)!.Version == "1.0.0", "Failed adapter update changed installed version");
                host.AssertUserFiles("adapters", AdapterId);
                return new { requested = "2.0.0", startup_failed = true, restored_running_version = "1.0.0" };
            });
        }
        finally
        {
            Updater.HttpClient = originalHttp;
            Updater.PluginUpdateApplier = null;
            await releases.StopAsync();
            await releases.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"All update integration scenarios passed. Report: {Path.Combine(root, "report.json")}");

        async Task Scenario(string name, Func<TestHost, Task<object>> test)
        {
            await using var host = await TestHost.CreateAsync(Path.Combine(root, name));
            try
            {
                var evidence = await test(host);
                report.Add(new { scenario = name, passed = true, evidence });
                Console.WriteLine($"PASS {name}: {JsonSerializer.Serialize(evidence)}");
            }
            catch (Exception ex)
            {
                report.Add(new { scenario = name, passed = false, error = ex.ToString() });
                Console.WriteLine($"FAIL {name}: {ex}");
                throw;
            }
        }
    }

    private static void MakePackage(string dll, string zip, int version, bool broken = false)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(dll, "payload/ShiroBot.UpdateProbe.dll");
        Write("payload/config.toml", "seed = 1\n");
        Write("payload/payload.txt", $"version-{version}");
        if (version == 1) Write("payload/old-only.txt", "removed-in-v2");
        else Write("payload/runtimes/sidecar.txt", "v2-sidecar");
        if (broken) Write("payload/fail-start", "fail");
        void Write(string name, string value) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(value); }
    }
    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ReleaseRedirectHandler(string origin) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "api.github.com") request.RequestUri = new Uri(origin + "/github" + request.RequestUri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class TestHost : IAsyncDisposable
    {
        public required string Root { get; init; }
        public required PluginManager Plugins { get; init; }
        public required HostEventDispatcher Dispatcher { get; init; }
        public required AdapterManager Adapters { get; init; }
        public required AdapterPackageManager AdapterPackages { get; init; }
        public required PluginRouteConfig Policy { get; init; }
        public required HostCommandHandler Commands { get; init; }
        public required HostHttpServer Server { get; init; }
        public required HttpClient Client { get; init; }

        public static async Task<TestHost> CreateAsync(string root)
        {
            var pluginRoot = Path.Combine(root, "plugins");
            var adapterRoot = Path.Combine(root, "adapters");
            Directory.CreateDirectory(pluginRoot);
            Directory.CreateDirectory(adapterRoot);
            var origin = $"http://127.0.0.1:{FreePort()}";
            var configPath = Path.Combine(root, "config.toml");
            await File.WriteAllTextAsync(configPath, "protocols = []\nhost_update_repository = \"\"\n");
            var configManager = new ConfigManager(configPath);
            var config = new CoreConfig { Api = new ApiHostConfig { ListenUrls = [origin], Auth = new ApiAuthConfig { Key = "integration-test-key" } } };
            var web = new WebHostContext(origin, true);
            var context = new BotContext(null, [], [], web);
            var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
            var logs = new HostLogHub();
            var shared = new SharedAssemblyResolver();
            var models = new ModelPackageRegistry(shared);
            var dispatcher = new HostEventDispatcher(new Lock(), context.ReplySubscriptions, runtime, logs);
            var plugins = new PluginManager(context, shared, models, runtime, logs) { PluginRootPath = pluginRoot };
            var adapters = new AdapterManager(adapterRoot, shared, models, context, new AdapterEventBridge(dispatcher), runtime, logs, _ => Task.CompletedTask);
            var packages = new AdapterPackageManager(adapterRoot);
            var coordinator = new ComponentReloadCoordinator(adapters, plugins, dispatcher, config.PluginRoutes);
            var commands = new HostCommandHandler(plugins, dispatcher, config.PluginRoutes, config, configManager, configPath);
            commands.SetAdapterCommands(adapters, coordinator, packages);
            plugins.EnableFileHotReload(dispatcher, config.PluginRoutes);
            Updater.PluginUpdateApplier = async (request, token) =>
            {
                var result = await PluginUpdateService.ApplyAsync(plugins, dispatcher, config.PluginRoutes, request.PluginName,
                    request.AssetDownloadUrl!, Path.GetFileName(new Uri(request.AssetDownloadUrl!).LocalPath), request.LatestVersion, token);
                Console.WriteLine(result.Message);
            };
            var server = await HostHttpServer.StartAsync(config.Api, configManager, configPath, plugins, dispatcher, config.PluginRoutes,
                web, runtime, logs, context, models, adapters, coordinator, packages, new HostPowerControl(() => { }));
            var client = new HttpClient { BaseAddress = new Uri(origin + "/api/v1/"), Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.Authorization = new("Bearer", "integration-test-key");
            return new TestHost { Root = root, Plugins = plugins, Dispatcher = dispatcher, Adapters = adapters,
                AdapterPackages = packages, Policy = config.PluginRoutes, Commands = commands, Server = server!, Client = client };
        }
        public async Task InstallOldAsync(string component, string zip)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(await File.ReadAllBytesAsync(zip)), "file", Path.GetFileName(zip));
            var upload = await ReadAsync(await Client.PostAsync(component + "/upload", content));
            await PostAsync($"/{component}/upload/{upload.GetProperty("upload_id").GetString()}/confirm", new { replace = false, enable = true });
        }
        public async Task<JsonElement> UpdateAdapterAsync(bool expectFailure = false)
        {
            var prepared = await PostAsync("/adapters/install/github", new { repository = "update-integration/adapter" });
            return await PostAsync($"/adapters/upload/{prepared.GetProperty("upload_id").GetString()}/confirm", new { replace = true, enable = true }, expectFailure);
        }
        public async Task<JsonElement> PostAsync(string path, object body, bool expectFailure = false) =>
            await ReadAsync(await Client.PostAsJsonAsync(path.TrimStart('/'), body), expectFailure);
        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, bool expectFailure = false)
        {
            using (response)
            {
                var text = await response.Content.ReadAsStringAsync();
                Check(response.IsSuccessStatusCode != expectFailure, $"Unexpected HTTP {(int)response.StatusCode}: {text}");
                return JsonDocument.Parse(text).RootElement.Clone();
            }
        }
        public Task<string> ConsoleUpdateAsync(params string[] arguments) =>
            (Task<string>)typeof(HostCommandHandler).GetMethod("HandleUpdateCommandAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Commands, [arguments])!;
        public void AssertPluginVersion(string version) =>
            Check(Plugins.GetLoadedPluginSnapshot().Single(plugin => plugin.Name == PluginId).Version == version, "Running plugin version did not match " + version);
        public void AssertAdapterVersion(string version) =>
            Check(Adapters.GetSnapshot().Single(adapter => adapter.Id == AdapterId).Version == version, "Running adapter version did not match " + version + ": " + JsonSerializer.Serialize(Adapters.GetSnapshot()));
        public void SeedUserFiles(string component, string id)
        {
            var folder = component == "adapters"
                ? Path.GetDirectoryName(AdapterPackages.Get(id)!.AssemblyPath)!
                : Path.Combine(Root, component, id);
            File.WriteAllText(Path.Combine(folder, "config.toml"), "user = 42 # retain me\n");
            Directory.CreateDirectory(Path.Combine(folder, "data"));
            File.WriteAllText(Path.Combine(folder, "data", "user.txt"), "keep-user-data");
        }
        public void AssertUserFiles(string component, string id)
        {
            var folder = component == "adapters"
                ? Path.GetDirectoryName(AdapterPackages.Get(id)!.AssemblyPath)!
                : Path.Combine(Root, component, id);
            Check(File.ReadAllText(Path.Combine(folder, "config.toml")) == "user = 42 # retain me\n", "User configuration was overwritten");
            Check(File.ReadAllText(Path.Combine(folder, "data", "user.txt")) == "keep-user-data", "User data was overwritten");
        }
        public void AssertPackage(string component, string id)
        {
            var folder = component == "adapters"
                ? Path.GetDirectoryName(AdapterPackages.Get(id)!.AssemblyPath)!
                : Path.Combine(Root, component, id);
            Check(File.ReadAllText(Path.Combine(folder, "payload.txt")) == "version-2", "Payload stayed on v1");
            Check(File.ReadAllText(Path.Combine(folder, "runtimes", "sidecar.txt")) == "v2-sidecar", "New sidecar was not installed");
            Check(!File.Exists(Path.Combine(folder, "old-only.txt")), "Removed package file remained installed");
            AssertUserFiles(component, id);
        }
        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await Plugins.UnloadAllAsync(Dispatcher);
            Plugins.BeginShutdown();
            await Adapters.StopForShutdownAsync();
            Client.Dispose();
        }
    }
}
