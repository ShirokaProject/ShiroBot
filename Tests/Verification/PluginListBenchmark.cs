using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;

internal static class PluginListBenchmark
{
    // Uses the production catalog endpoint over real loopback HTTP. No plugin code runs.
    internal static async Task RunAsync(string fixtures, int requests)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requests);
        var resolver = new SharedAssemblyResolver();
        var context = new BotContext(null, [], [], new WebHostContext("", false));
        var manager = new PluginManager(context, resolver, new ModelPackageRegistry(resolver),
            new HostRuntimeState(DateTimeOffset.UtcNow), new HostLogHub())
            { PluginRootPath = Path.GetFullPath(fixtures) };
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        typeof(HostHttpServer).GetMethod("MapPluginCatalogEndpoints", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [app.MapGroup("/api/v1"), manager]);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        const string endpoint = "/api/v1/plugins/list";
        var expected = await client.GetStringAsync(endpoint);
        using var expectedDocument = JsonDocument.Parse(expected);
        var pluginCount = expectedDocument.RootElement.GetArrayLength();
        if (pluginCount == 0) throw new InvalidOperationException("Benchmark requires at least one plugin fixture.");
        for (var i = 0; i < 100; i++) await client.GetStringAsync(endpoint);
        for (var trial = 1; trial <= 3; trial++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var liveBefore = GC.GetTotalMemory(false);
            var allocatedBefore = GC.GetTotalAllocatedBytes(true);
            var gen0 = GC.CollectionCount(0);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var rssBefore = process.WorkingSet64;
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < requests; i++)
            {
                var response = await client.GetStringAsync(endpoint);
                if (response != expected) throw new InvalidOperationException("Plugin list changed during benchmark.");
            }
            watch.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
            var collections = GC.CollectionCount(0) - gen0;
            var heapBeforeGc = GC.GetTotalMemory(false);
            process.Refresh();
            var rssAfter = process.WorkingSet64;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var liveAfter = GC.GetTotalMemory(false);
            Console.WriteLine(JsonSerializer.Serialize(new {
                trial, requests, elapsedMs = watch.Elapsed.TotalMilliseconds,
                allocatedBytes = allocated, allocatedBytesPerRequest = allocated / requests,
                gen0Collections = collections, liveBefore, heapBeforeGc, liveAfter,
                rssBefore, rssAfter,
                responseSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected))),
                pluginCount
            }));
        }
        // Verify additions and removal are visible without recreating the manager.
        var addedDirectory = Path.Combine(fixtures, "benchmark-added");
        if (Directory.Exists(addedDirectory) || File.Exists(addedDirectory))
            throw new InvalidOperationException("Use an isolated fixture directory without benchmark-added.");
        Directory.CreateDirectory(addedDirectory);
        try
        {
            var source = Directory.EnumerateFiles(fixtures, "*.dll", SearchOption.AllDirectories)
                .First(path => manager.TryProbePluginInfoFile(path) is not null);
            var target = Path.Combine(addedDirectory, "Added.dll");
            File.Copy(source, target);
            var added = await client.GetStringAsync(endpoint);
            if (added == expected) throw new InvalidOperationException("New plugin DLL was not discovered.");
            File.WriteAllText(target, "not a managed DLL");
            if (await client.GetStringAsync(endpoint) != expected)
                throw new InvalidOperationException("Changed DLL kept stale plugin metadata.");
            File.Delete(target);
            if (await client.GetStringAsync(endpoint) != expected)
                throw new InvalidOperationException("Deleted DLL kept stale plugin metadata.");
        }
        finally { Directory.Delete(addedDirectory, recursive: true); }
        Console.WriteLine("Plugin list addition, modification and deletion verification passed.");
        await app.StopAsync();
    }
}
