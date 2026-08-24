using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace ShiroBot.Hosting;

internal sealed class HostLogHub
{
    private const int MaxHistory = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();
    private readonly Queue<LogEntry> _history = new();
    private readonly ConcurrentDictionary<Guid, Channel<LogEntry>> _subscribers = new();
    private readonly ConcurrentDictionary<string, LogSourceInfo> _sources = new(StringComparer.OrdinalIgnoreCase);

    public HostLogHub()
    {
        RegisterSource("system", "系统日志", "system");
    }

    public void Record(string source, string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "system" : source.Trim();
        EnsureSource(normalizedSource);

        var entry = new LogEntry
        {
            Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Source = normalizedSource,
            Level = NormalizeLevel(level),
            Message = message
        };

        lock (_lock)
        {
            _history.Enqueue(entry);
            while (_history.Count > MaxHistory)
            {
                _history.Dequeue();
            }
        }

        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(entry);
        }
    }

    public void RegisterSource(string source, string description, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return;

        var normalizedSource = source.Trim();
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedSource : displayName.Trim();
        _sources[normalizedSource] = new LogSourceInfo
        {
            Source = normalizedSource,
            Description = string.IsNullOrWhiteSpace(description) ? GetDefaultDescription(normalizedSource) : description,
            PluginName = normalizedDisplayName
        };
    }

    private void EnsureSource(string source)
    {
        _sources.TryAdd(source, new LogSourceInfo
        {
            Source = source,
            Description = GetDefaultDescription(source),
            PluginName = source
        });
    }

    public LogSourceInfo[] GetSources() => _sources.Values
        .OrderBy(source => source.Source.Equals("system", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(source => source.Source, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public LogEntry[] GetHistory(string source, int tail)
    {
        LogEntry[] snapshot;
        lock (_lock)
        {
            snapshot = _history.ToArray();
        }

        var query = snapshot.AsEnumerable();
        if (!source.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(entry => entry.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        }

        return query.TakeLast(Math.Clamp(tail, 0, MaxHistory)).ToArray();
    }

    public async Task StreamAsync(WebSocket webSocket, string source, int tail, CancellationToken cancellationToken)
    {
        source = string.IsNullOrWhiteSpace(source) ? "all" : source.Trim();
        tail = Math.Clamp(tail, 0, MaxHistory);

        await SendAsync(webSocket, new { type = "connected", source, tail }, cancellationToken).ConfigureAwait(false);
        await SendAsync(webSocket, new { type = "history", data = GetHistory(source, tail) }, cancellationToken).ConfigureAwait(false);

        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (webSocket.State != WebSocketState.Open) break;
                if (!source.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                    !entry.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await SendAsync(webSocket, new { type = "logs", data = new[] { entry } }, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    private static async Task SendAsync(WebSocket webSocket, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeLevel(string level) => level.ToLowerInvariant() switch
    {
        "info" => "info",
        "warning" => "warning",
        "error" => "error",
        "success" => "success",
        _ => "log"
    };

    private static string GetDefaultDescription(string source) =>
        source.Equals("system", StringComparison.OrdinalIgnoreCase) ? "系统日志" : $"{source} 日志";

    internal sealed class LogSourceInfo
    {
        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("plugin_name")]
        public string? PluginName { get; init; }
    }

    internal sealed class LogEntry
    {
        [JsonPropertyName("time")]
        public string Time { get; init; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("level")]
        public string Level { get; init; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }
}
