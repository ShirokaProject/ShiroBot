using System.Collections;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;

internal static class LocalLogVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shirobot-local-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VerifyStorage(Path.Combine(root, "storage"));
            VerifyRetention(Path.Combine(root, "retention"));
            VerifyRotationAndCorruption(Path.Combine(root, "rotate"));
            VerifyFailure(Path.Combine(root, "failure"));
            await VerifyConfigAsync(Path.Combine(root, "config"));
            await VerifyStreamAsync(Path.Combine(root, "stream"));
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS local logs: disk-only history, migration, restart, filters, UTF-8/multiline, rotation, retention/cleanup, failure fallback, hot config, WebSocket history/live boundary and bounded clients.");
    }

    private static void VerifyStorage(string root)
    {
        if (!new LocalLogConfig().SaveToFile || new LocalLogConfig().RetentionDays != 3)
            throw new Exception("Local logs must default to ON with 3-day retention");
        using var hub = new HostLogHub();
        hub.Configure(new() { SaveToFile = false }, root);
        if (Directory.Exists(Path.Combine(root, "logs"))) throw new Exception("Disabled disk logs created a directory");
        hub.Record("system", "info", "before switch");
        hub.Configure(new() { SaveToFile = true }, root);
        if (!hub.IsFileLogging || hub.MemoryHistoryCount != 0) throw new Exception("Enabling disk logs retained memory history");
        var retained = RecordPayload(hub);
        hub.Record("plugin", "warning", "中文😀\n第二行\"quote\"");
        Parallel.For(0, 1200, i => hub.Record(i % 2 == 0 ? "adapter" : "plugin", "info", "record-" + i));
        if (hub.MemoryHistoryCount != 0 || hub.GetHistory("all", 5000).Length != 1000)
            throw new Exception("Disk-only history count changed");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        if (retained.IsAlive) throw new Exception("Disk logging kept a logged payload alive");
        var filtered = hub.GetHistory("PLUGIN", 900);
        if (filtered.Length != 601 || filtered[0].Message != "中文😀\n第二行\"quote\"" || filtered.Any(entry => entry.Source != "plugin"))
            throw new Exception("UTF-8/multiline or source-filtered disk history changed");
        hub.Configure(new() { SaveToFile = false }, root);
        hub.Record("system", "success", "memory only");
        if (hub.IsFileLogging || hub.MemoryHistoryCount != 1 || hub.GetHistory("all", 1000).Single().Message != "memory only")
            throw new Exception("Disabling local logs did not return to bounded memory history");
        hub.Configure(new() { SaveToFile = true }, root);
        if (hub.MemoryHistoryCount != 0 || hub.GetHistory("all", 1).Single().Message != "memory only")
            throw new Exception("Re-enabling local logs lost migrated memory history");
        hub.Dispose();
        using var restarted = new HostLogHub();
        restarted.Configure(new() { SaveToFile = true }, root);
        if (restarted.GetHistory("all", 1).Single().Message != "memory only" || restarted.MemoryHistoryCount != 0)
            throw new Exception("Restart lost persisted history");
        var files = Directory.GetFiles(Path.Combine(root, "logs"));
        var first = File.ReadLines(files[0]).First();
        if (JsonDocument.Parse(first).RootElement.TryGetProperty("sequence", out _))
            throw new Exception("Internal WebSocket sequence leaked into the log format");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RecordPayload(HostLogHub hub)
    {
        var payload = new string('z', 16000);
        hub.Record("system", "info", payload);
        return new WeakReference(payload);
    }

    private static void VerifyRetention(string root)
    {
        var clock = new TestClock();
        using var hub = new HostLogHub(clock);
        hub.Configure(new() { SaveToFile = true, RetentionDays = 2 }, root);
        hub.Record("system", "info", "day-one");
        clock.Advance(TimeSpan.FromDays(1)); hub.Record("system", "info", "day-two");
        clock.Advance(TimeSpan.FromDays(1)); hub.Record("system", "info", "day-three");
        if (!hub.GetHistory("all", 10).Select(entry => entry.Message).SequenceEqual(["day-two", "day-three"]))
            throw new Exception("Retention kept expired records");
        var directory = Path.Combine(root, "logs");
        File.WriteAllText(Path.Combine(directory, "keep.txt"), "unrelated");
        File.WriteAllText(Path.Combine(directory, "shirobot-manual.jsonl"), "unrelated");
        hub.Configure(new() { SaveToFile = true, RetentionDays = 1 }, root);
        if (hub.GetHistory("all", 10).Single().Message != "day-three") throw new Exception("Retention changes did not apply immediately");
        clock.Advance(TimeSpan.FromDays(3)); hub.CleanupFiles();
        if (hub.GetHistory("all", 10).Length != 0 || Directory.GetFiles(directory).Length != 2)
            throw new Exception("Idle cleanup failed or deleted unrelated files");
        hub.Record("system", "info", "after-idle");
        if (hub.GetHistory("all", 10).Single().Message != "after-idle") throw new Exception("Writer did not recover after idle cleanup");
    }

    private static void VerifyRotationAndCorruption(string root)
    {
        using var hub = new HostLogHub();
        hub.Configure(new() { SaveToFile = true }, root);
        var large = new string('x', 32768);
        for (var i = 0; i < 280; i++) hub.Record("system", "info", i + large);
        var files = Directory.GetFiles(Path.Combine(root, "logs"), "*.jsonl");
        if (files.Length < 2 || hub.GetHistory("all", 1000).Length != 280 || hub.MemoryHistoryCount != 0)
            throw new Exception("Rotation lost history or retained it in memory");
        hub.Dispose();
        var latest = files.Order(StringComparer.Ordinal).Last();
        File.AppendAllText(latest, "{\"truncated\":");
        using var restart = new HostLogHub();
        restart.Configure(new() { SaveToFile = true }, root);
        restart.Record("system", "info", "after-crash");
        if (restart.GetHistory("all", 1000).Length != 281 || restart.GetHistory("all", 1).Single().Message != "after-crash")
            throw new Exception("An incomplete final record corrupted subsequent logs");
    }

    private static void VerifyFailure(string root)
    {
        Directory.CreateDirectory(root);
        var blocked = Path.Combine(root, "logs");
        File.WriteAllText(blocked, "not a directory");
        using var hub = new HostLogHub();
        hub.Configure(new() { SaveToFile = true }, root);
        for (var i = 0; i < 1100; i++) hub.Record("system", "info", i.ToString());
        if (hub.IsFileLogging || hub.MemoryHistoryCount != 1000 || hub.GetHistory("all", 1).Single().Message != "1099")
            throw new Exception("File failure lost live memory history or bypassed bounds");
        File.Delete(blocked);
        hub.Configure(new() { SaveToFile = true }, root);
        if (!hub.IsFileLogging || hub.MemoryHistoryCount != 0) throw new Exception("Disk logging could not recover when reconfigured");
    }

    private static async Task VerifyConfigAsync(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.toml");
        var manager = new ConfigManager(path);
        var active = new CoreConfig { Logs = new() { SaveToFile = false } };
        manager.SaveConfig(path, active);
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
        using var hub = new HostLogHub();
        using var watch = new CoreConfigWatcher(path, active, bot, hub, root);
        using var document = JsonDocument.Parse("{\"logs\":{\"save_to_file\":true,\"retention_days\":3}}");
        var apply = typeof(HostHttpServer).GetMethod("ApplyConfigPatch", BindingFlags.Static | BindingFlags.NonPublic)!;
        apply.Invoke(null, [document.RootElement, active.Api, manager, path]);
        for (var attempt = 0; attempt < 100 && !hub.IsFileLogging; attempt++) await Task.Delay(50);
        if (!hub.IsFileLogging || active.Logs.RetentionDays != 3) throw new Exception("Local log settings did not hot reload");
        var saved = File.ReadAllText(path);
        hub.Record("system", "info", "hot file entry");
        foreach (var json in new[] { "{\"logs\":{\"save_to_file\":false,\"retention_days\":0}}", "{\"logs\":{\"retention_days\":1.5}}", "{\"logs\":{\"save_to_file\":\"true\"}}", "{\"logs\":null}" })
        {
            using var invalid = JsonDocument.Parse(json);
            try { apply.Invoke(null, [invalid.RootElement, active.Api, manager, path]); throw new Exception("Invalid log settings accepted"); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
            if (File.ReadAllText(path) != saved) throw new Exception("Rejected log config partially modified the file");
        }
        var schema = typeof(HostHttpServer).GetMethod("GetCoreConfigSchema", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        var fields = JsonSerializer.SerializeToElement(schema, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (!fields.EnumerateArray().Any(item => item.GetProperty("key").GetString() == "logs" && item.GetProperty("group_id").GetString() == "logs"))
            throw new Exception("Config center did not expose a logs category");
        manager.SetConfigValue(path, "logs.save_to_file", false);
        for (var attempt = 0; attempt < 100 && hub.IsFileLogging; attempt++) await Task.Delay(50);
        if (hub.IsFileLogging || active.Logs.SaveToFile) throw new Exception("Disabling file logs did not hot reload");
    }

    private static async Task VerifyStreamAsync(string root)
    {
        using var hub = new HostLogHub();
        hub.Configure(new() { SaveToFile = true }, root);
        hub.Record("system", "info", "disk-history");
        using var socket = new FixtureSocket();
        using var cancellation = new CancellationTokenSource();
        var streaming = hub.StreamAsync(socket, "all", 10, cancellation.Token);
        await socket.InitialSendStarted.Task;
        // Log while the initial WebSocket response is blocked: the subscriber must
        // already exist so the history/live boundary cannot lose this message.
        hub.Record("system", "info", "between-history-and-live");
        socket.AllowInitialSend.TrySetResult();
        await socket.LiveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!socket.Messages.Any(text => text.Contains("disk-history", StringComparison.Ordinal)) ||
            socket.Messages.Count(text => text.Contains("between-history-and-live", StringComparison.Ordinal)) != 1 || hub.MemoryHistoryCount != 0)
            throw new Exception("Disk history/live streaming lost or duplicated entries");
        socket.ReceiveCloseRequested.TrySetResult();
        await streaming.WaitAsync(TimeSpan.FromSeconds(5));
        var remaining = (IDictionary)typeof(HostLogHub).GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub)!;
        if (remaining.Count != 0) throw new Exception("Closing the WebSocket retained its live log queue");

        using var slow = new FixtureSocket { BlockLive = true };
        using var stopSlow = new CancellationTokenSource();
        slow.AllowInitialSend.TrySetResult();
        var slowStreaming = hub.StreamAsync(slow, "all", 0, stopSlow.Token);
        hub.Record("system", "info", "blocked-live");
        await slow.LiveBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var subscribers = (IDictionary)typeof(HostLogHub).GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub)!;
        var queue = (System.Threading.Channels.Channel<HostLogHub.LogEntry>)subscribers.Values.Cast<object>().Single();
        for (var i = 0; i < 1200; i++) hub.Record("system", "info", "queued-" + i);
        if (queue.Reader.Count > 1000 || subscribers.Count != 0 || hub.MemoryHistoryCount != 0)
            throw new Exception("A slow disk-log client kept accepting unbounded log history");
        stopSlow.Cancel();
        try { await slowStreaming; } catch (OperationCanceledException) when (stopSlow.IsCancellationRequested) { }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class FixtureSocket : WebSocket
    {
        public bool BlockLive { get; init; }
        public readonly TaskCompletionSource LiveBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<string> Messages = [];
        public readonly TaskCompletionSource InitialSendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource AllowInitialSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource LiveReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReceiveCloseRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override WebSocketState State => WebSocketState.Open;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            await ReceiveCloseRequested.Task.WaitAsync(token);
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        {
            var text = Encoding.UTF8.GetString(buffer);
            if (Messages.Count == 0) { InitialSendStarted.TrySetResult(); await AllowInitialSend.Task.WaitAsync(token); }
            if (BlockLive && text.Contains("blocked-live", StringComparison.Ordinal))
            {
                LiveBlocked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            Messages.Add(text);
            if (text.Contains("between-history-and-live", StringComparison.Ordinal)) LiveReceived.TrySetResult();
        }
    }
}
