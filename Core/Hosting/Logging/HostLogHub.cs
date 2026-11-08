using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Globalization;
using ShiroBot.Configuration;

namespace ShiroBot.Hosting.Logging;

internal sealed class HostLogHub : IDisposable
{
    private const int MaxHistory = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();
    private readonly Queue<LogEntry> _history = new();
    private readonly ConcurrentDictionary<Guid, Channel<LogEntry>> _subscribers = new();
    private readonly ConcurrentDictionary<string, LogSourceInfo> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;
    private LocalLogStore? _files;
    private ITimer? _cleanupTimer;
    private long _sequence;
    private bool _disposed;

    public HostLogHub(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        RegisterSource("system", "系统日志", "System", LogSourceKind.System);
    }

    internal bool IsFileLogging { get { lock (_lock) return _files is not null; } }
    internal int MemoryHistoryCount { get { lock (_lock) return _history.Count; } }

    internal void Configure(LocalLogConfig config, string hostDirectory)
    {
        config.Validate();
        lock (_lock)
        {
            if (_disposed) return;
            if (!config.SaveToFile)
            {
                _cleanupTimer?.Dispose(); _cleanupTimer = null;
                try { _files?.Dispose(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { FallBackToMemory(error); }
                _files = null;
                return;
            }
            try
            {
                if (_files is null)
                {
                    _files = new LocalLogStore(Path.Combine(hostDirectory, "logs"), config.RetentionDays, _clock);
                    foreach (var entry in _history) _files.Append(entry);
                    // Disk is the history store: do not retain a second copy of message strings.
                    _history.Clear();
                }
                _files.RetentionDays = config.RetentionDays;
                _files.Cleanup();
                _cleanupTimer ??= _clock.CreateTimer(static state =>
                {
                    if (((WeakReference<HostLogHub>)state!).TryGetTarget(out var hub)) hub.CleanupFiles();
                }, new WeakReference<HostLogHub>(this), TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FallBackToMemory(error);
            }
        }
    }

    internal void CleanupFiles()
    {
        lock (_lock)
        {
            if (_disposed || _files is null) return;
            try { _files.Cleanup(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { FallBackToMemory(error); }
        }
    }

    private void FallBackToMemory(Exception error)
    {
        try { _files?.Dispose(); }
        catch (Exception closeError) when (closeError is IOException or UnauthorizedAccessException) { }
        _files = null;
        _cleanupTimer?.Dispose(); _cleanupTimer = null;
        // Never log through BotLog here: that would recurse into the failed writer.
        Console.Error.WriteLine($"[日志] 本地日志写入失败，已退回最近 {MaxHistory} 条内存历史：{error.Message}");
    }

    public void Record(string source, string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "system" : source.Trim();
        EnsureSource(normalizedSource);

        lock (_lock)
        {
            if (_disposed) return;
            var entry = new LogEntry
            {
                Sequence = ++_sequence,
                Time = _clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Source = normalizedSource,
                Level = NormalizeLevel(level),
                // Bound both disk lines and slow-client queues; avoid retaining huge payload logs.
                Message = message.Length <= 32768 ? message : message[..32768] + " [日志过长，已截断]"
            };
            if (_files is not null)
            {
                try { _files.Append(entry); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { FallBackToMemory(error); }
            }
            if (_files is null)
            {
                _history.Enqueue(entry);
                while (_history.Count > MaxHistory) _history.Dequeue();
            }
            foreach (var (id, subscriber) in _subscribers)
            {
                if (!subscriber.Writer.TryWrite(entry))
                {
                    subscriber.Writer.TryComplete(new IOException("Log client is too slow; reconnect to retrieve recent history."));
                    _subscribers.TryRemove(id, out _);
                }
            }
        }
    }

    public void RegisterSource(string source, string description, string? displayName = null, string kind = LogSourceKind.System)
    {
        if (string.IsNullOrWhiteSpace(source)) return;

        var normalizedSource = source.Trim();
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedSource : displayName.Trim();
        _sources[normalizedSource] = new LogSourceInfo
        {
            Source = normalizedSource,
            Description = string.IsNullOrWhiteSpace(description) ? GetDefaultDescription(normalizedSource) : description,
            PluginName = normalizedDisplayName,
            Kind = kind
        };
    }

    private void EnsureSource(string source)
    {
        _sources.GetOrAdd(source, static key => new LogSourceInfo
        {
            Source = key,
            Description = GetDefaultDescription(key),
            PluginName = key
        });
    }

    public LogSourceInfo[] GetSources() => _sources.Values
        .OrderBy(source => source.Source.Equals("system", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(source => source.Source, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public LogEntry[] GetHistory(string source, int tail)
    {
        lock (_lock)
        {
            tail = Math.Clamp(tail, 0, MaxHistory);
            if (_files is not null)
            {
                try { return _files.ReadHistory(source, tail); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { FallBackToMemory(error); }
            }
            return _history.Where(entry => source.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                entry.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).TakeLast(tail).ToArray();
        }
    }

    public async Task StreamAsync(WebSocket webSocket, string source, int tail, CancellationToken cancellationToken)
    {
        using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var streamToken = disconnected.Token;
        source = string.IsNullOrWhiteSpace(source) ? "all" : source.Trim();
        tail = Math.Clamp(tail, 0, MaxHistory);

        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(MaxHistory)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        LogEntry[] history;
        long boundary;
        lock (_lock)
        {
            if (_disposed) return;
            _subscribers[id] = channel;
            history = GetHistory(source, tail);
            boundary = _sequence;
        }
        var closed = WatchCloseAsync(webSocket, disconnected);

        try
        {
            await SendAsync(webSocket, new { type = "connected", source, tail }, streamToken).ConfigureAwait(false);
            await SendAsync(webSocket, new { type = "history", data = history }, streamToken).ConfigureAwait(false);
            history = [];
            await foreach (var entry in channel.Reader.ReadAllAsync(streamToken).ConfigureAwait(false))
            {
                if (webSocket.State != WebSocketState.Open) break;
                if (entry.Sequence <= boundary) continue;
                if (!source.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                    !entry.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await SendAsync(webSocket, new { type = "logs", data = new[] { entry } }, streamToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (disconnected.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
            disconnected.Cancel();
            await closed.ConfigureAwait(false);
            if (webSocket.State == WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
            }
        }
    }

    private static async Task WatchCloseAsync(WebSocket socket, CancellationTokenSource disconnected)
    {
        try
        {
            var buffer = new byte[256];
            while (!disconnected.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var frame = await socket.ReceiveAsync(buffer.AsMemory(), disconnected.Token).ConfigureAwait(false);
                if (frame.MessageType == WebSocketMessageType.Close) break;
            }
            disconnected.Cancel();
        }
        catch (NotSupportedException) { /* Send-only test WebSockets have no receive side. */ }
        catch (OperationCanceledException) when (disconnected.IsCancellationRequested) { }
        catch (Exception error) when (error is WebSocketException or ObjectDisposedException)
        {
            disconnected.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupTimer?.Dispose(); _cleanupTimer = null;
            try { _files?.Dispose(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { FallBackToMemory(error); }
            _files = null;
            _history.Clear();
            foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryComplete();
            _subscribers.Clear();
        }
    }

    private static async Task SendAsync(WebSocket webSocket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
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

    /// <summary>What produced a log source, so the Dashboard can group adapters and plugins apart.</summary>
    internal static class LogSourceKind
    {
        public const string System = "system";
        public const string Adapter = "adapter";
        public const string Plugin = "plugin";
    }

    internal sealed class LogSourceInfo
    {
        /// <summary>system, adapter or plugin.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; init; } = LogSourceKind.System;

        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("plugin_name")]
        public string? PluginName { get; init; }
    }

    internal sealed class LogEntry
    {
        [JsonIgnore]
        public long Sequence { get; init; }
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
