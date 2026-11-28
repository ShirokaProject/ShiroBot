using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;

namespace ShiroBot.Verification;

internal static class LogSseVerification
{
    public static async Task RunAsync()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)reservation.LocalEndpoint).Port}";
        reservation.Stop();
        using var hub = new HostLogHub();
        hub.Record("fixture", "info", "initial");
        var web = new WebHostContext(origin, false);
        var bot = new BotContext(null, [], [], web, hub);
        var endpoints = new HostHttpServer.DiagnosticsEndpoints(bot, null!, hub);
        var config = new ApiHostConfig { Enable = true, EnableDashboard = false, ListenUrls = [origin], Token = "sse-test" };
        await using var server = new HostHttpServer(Options.Create(config), [endpoints], web, endpoints);
        await server.StartAsync(CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri(origin) };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using (var denied = await client.GetAsync("/api/v1/logs/stream", deadline.Token))
            if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("SSE lost API authorization.");
        using (var response = await client.GetAsync("/api/v1/logs/stream?token=sse-test&source=fixture&tail=1",
                   HttpCompletionOption.ResponseHeadersRead, deadline.Token))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType != "text/event-stream" ||
                response.Headers.GetValues("X-Accel-Buffering").Single() != "no")
                throw new Exception("SSE headers permit buffering or have the wrong content type.");
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
            using var connected = await ReadEvent(reader, deadline.Token);
            using var history = await ReadEvent(reader, deadline.Token);
            if (connected.RootElement.GetProperty("type").GetString() != "connected" ||
                history.RootElement.GetProperty("data")[0].GetProperty("message").GetString() != "initial")
                throw new Exception("SSE initial history was not delivered.");
            var timer = Stopwatch.StartNew();
            hub.Record("other", "info", "excluded");
            hub.Record("fixture", "info", "中文😀\nsecond line");
            using var live = await ReadEvent(reader, deadline.Token);
            if (live.RootElement.GetProperty("data").GetArrayLength() != 1 ||
                live.RootElement.GetProperty("data")[0].GetProperty("message").GetString() != "中文😀\nsecond line")
                throw new Exception("SSE source filtering or multiline framing failed.");
            Console.WriteLine($"SSE loopback record-to-receive: {timer.Elapsed.TotalMilliseconds:F2} ms");
            string? heartbeat;
            do { heartbeat = await reader.ReadLineAsync(deadline.Token); }
            while (heartbeat is not null && !heartbeat.StartsWith(':'));
            if (heartbeat != ": keepalive") throw new Exception("Idle SSE did not send a heartbeat.");
        }
        var subscribers = (IDictionary)typeof(HostLogHub).GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub)!;
        for (var attempt = 0; attempt < 100 && subscribers.Count != 0; attempt++) await Task.Delay(20, deadline.Token);
        if (subscribers.Count != 0) throw new Exception("Disconnect retained an SSE subscription.");
        // Native EventSource reconnects with a fresh history; the client replaces its old history.
        using (var response = await client.GetAsync("/api/v1/logs/stream?token=sse-test&source=fixture&tail=1",
                   HttpCompletionOption.ResponseHeadersRead, deadline.Token))
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
            using var connected = await ReadEvent(reader, deadline.Token);
            using var history = await ReadEvent(reader, deadline.Token);
            if (history.RootElement.GetProperty("data")[0].GetProperty("message").GetString() != "中文😀\nsecond line")
                throw new Exception("SSE reconnect lost recent history.");
        }
        Console.WriteLine("PASS SSE HTTP auth, headers, history/live filtering, UTF-8/multiline, heartbeat, disconnect and reconnect.");
    }

    private static async Task<JsonDocument> ReadEvent(StreamReader reader, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line)
            if (line.StartsWith("data: ", StringComparison.Ordinal)) return JsonDocument.Parse(line[6..]);
        throw new EndOfStreamException("SSE ended before the expected event.");
    }
}
