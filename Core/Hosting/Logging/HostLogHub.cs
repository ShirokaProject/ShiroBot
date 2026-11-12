using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ShiroBot.Configuration;

namespace ShiroBot.Hosting.Logging;

internal sealed class HostLogHub : IDisposable
{
    private const int MaxHistory = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock;
    private readonly Queue<LogEntry> _history = new();

    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<string, LogSourceInfo> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, Channel<LogEntry>> _subscribers = new();
    private ITimer? _cleanupTimer;
    private ITimer? _flushTimer;
    private bool _disposed;
    private LocalLogStore? _files;
    private long _sequence;

    public HostLogHub(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        RegisterSource("system", "系统日志", "System");
    }

    internal bool IsFileLogging
    {
        get
        {
            lock (_lock)
            {
                return _files is not null;
            }
        }
    }

    internal int MemoryHistoryCount
    {
        get
        {
            lock (_lock)
            {
                return _history.Count;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
            _flushTimer?.Dispose();
            _flushTimer = null;
            try
            {
                _files?.Dispose();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FallBackToMemory(error);
            }

            _files = null;
            _history.Clear();
            foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryComplete();
            _subscribers.Clear();
        }
    }

    internal void Configure(LocalLogConfig config, string hostDirectory)
    {
        config.Validate();
        lock (_lock)
        {
            if (_disposed) return;
            if (!config.SaveToFile)
            {
                _cleanupTimer?.Dispose();
                _cleanupTimer = null;
                _flushTimer?.Dispose();
                _flushTimer = null;
                try
                {
                    _files?.Dispose();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    FallBackToMemory(error);
                }

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

                _flushTimer ??= _clock.CreateTimer(static state =>
                {
                    if (((WeakReference<HostLogHub>)state!).TryGetTarget(out var hub)) hub.FlushFiles();
                }, new WeakReference<HostLogHub>(this), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
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
            try
            {
                _files.Cleanup();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FallBackToMemory(error);
            }
        }
    }

    private void FlushFiles()
    {
        lock (_lock)
        {
            if (_disposed || _files is null) return;
            try { _files.Flush(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FallBackToMemory(error);
            }
        }
    }

    private void FallBackToMemory(Exception error)
    {
        try
        {
            _files?.Dispose();
        }
        catch (Exception closeError) when (closeError is IOException or UnauthorizedAccessException)
        {
        }

        _files = null;
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        _flushTimer?.Dispose();
        _flushTimer = null;
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
            foreach (var (id, subscriber) in _subscribers)
                if (!subscriber.Writer.TryWrite(entry))
                {
                    subscriber.Writer.TryComplete(
                        new IOException("Log client is too slow; reconnect to retrieve recent history."));
                    _subscribers.TryRemove(id, out _);
                }

            if (_files is not null)
                try
                {
                    _files.Append(entry);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    FallBackToMemory(error);
                }

            if (_files is null)
            {
                _history.Enqueue(entry);
                while (_history.Count > MaxHistory) _history.Dequeue();
            }
        }
    }

    public void RegisterSource(string source, string description, string? displayName = null,
        string kind = LogSourceKind.System)
    {
        if (string.IsNullOrWhiteSpace(source)) return;

        var normalizedSource = source.Trim();
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedSource : displayName.Trim();
        _sources[normalizedSource] = new LogSourceInfo
        {
            Source = normalizedSource,
            Description =
                string.IsNullOrWhiteSpace(description) ? GetDefaultDescription(normalizedSource) : description,
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

    public LogSourceInfo[] GetSources()
    {
        return
        [
            .. _sources.Values
                .OrderBy(source => source.Source.Equals("system", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(source => source.Source, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public LogEntry[] GetHistory(string source, int tail)
    {
        Func<LogEntry[]> read;
        lock (_lock) read = CaptureHistory(source, Math.Clamp(tail, 0, MaxHistory));
        return read();
    }

    // Capture file lengths under the publication lock; scan them after releasing it.
    // Records appended during the scan are delivered only through the live queue.
    private Func<LogEntry[]> CaptureHistory(string source, int tail)
    {
        if (_files is not null)
        {
            var files = _files;
            try
            {
                var snapshot = files.CaptureHistory(source, tail);
                return () =>
                {
                    try { return snapshot(); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        lock (_lock)
                        {
                            if (ReferenceEquals(_files, files)) FallBackToMemory(error);
                            return ReadMemoryHistory(source, tail);
                        }
                    }
                };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FallBackToMemory(error);
            }
        }

        var memory = ReadMemoryHistory(source, tail);
        return () => memory;
    }

    private LogEntry[] ReadMemoryHistory(string source, int tail) =>
        [.. _history.Where(entry => source.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                   entry.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).TakeLast(tail)];

    public async Task StreamAsync(Stream stream, string source, int tail, CancellationToken cancellationToken)
    {
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = stopped.Token;
        source = string.IsNullOrWhiteSpace(source) ? "all" : source.Trim();
        tail = Math.Clamp(tail, 0, MaxHistory);
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(MaxHistory)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
        });
        Func<LogEntry[]> readHistory;
        long boundary;
        lock (_lock)
        {
            if (_disposed) return;
            readHistory = CaptureHistory(source, tail);
            boundary = _sequence;
            _subscribers[id] = channel;
        }

        try
        {
            await SendAsync(stream, new { type = "connected", source, tail }, token).ConfigureAwait(false);
            var history = readHistory();
            readHistory = null!; // Release snapshot references during an idle connection.
            await SendAsync(stream, new { type = "history", data = history }, token).ConfigureAwait(false);
            history = [];
            var batch = new List<LogEntry>(100);
            var available = channel.Reader.WaitToReadAsync(token).AsTask();
            using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var tick = heartbeat.WaitForNextTickAsync(token).AsTask();
            while (true)
            {
                if (await Task.WhenAny(available, tick).ConfigureAwait(false) == tick)
                {
                    if (!await tick.ConfigureAwait(false)) break;
                    await SendAsync(stream, null, token).ConfigureAwait(false);
                    tick = heartbeat.WaitForNextTickAsync(token).AsTask();
                    continue;
                }

                if (!await available.ConfigureAwait(false)) break;
                while (batch.Count < 100 && channel.Reader.TryRead(out var entry))
                    if (entry.Sequence > boundary && (source.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                        entry.Source.Equals(source, StringComparison.OrdinalIgnoreCase))) batch.Add(entry);
                if (batch.Count > 0)
                {
                    await SendAsync(stream, new { type = "logs", data = batch }, token).ConfigureAwait(false);
                    batch.Clear();
                }
                available = channel.Reader.WaitToReadAsync(token).AsTask();
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
            await stopped.CancelAsync().ConfigureAwait(false);
        }
    }

    private static async Task SendAsync(Stream stream, object? payload, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var frame = payload is null ? ": keepalive\n\n" : "data: " + JsonSerializer.Serialize(payload, JsonOptions) + "\n\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(frame), timeout.Token).ConfigureAwait(false);
        await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private static string NormalizeLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "info" => "info",
            "warning" => "warning",
            "error" => "error",
            "success" => "success",
            _ => "log"
        };
    }

    private static string GetDefaultDescription(string source)
    {
        return source.Equals("system", StringComparison.OrdinalIgnoreCase) ? "系统日志" : $"{source} 日志";
    }

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

        [JsonPropertyName("source")] public string Source { get; init; } = string.Empty;

        [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;

        [JsonPropertyName("plugin_name")] public string? PluginName { get; init; }
    }

    internal sealed class LogEntry
    {
        [JsonIgnore] public long Sequence { get; init; }

        [JsonPropertyName("time")] public string Time { get; init; } = string.Empty;

        [JsonPropertyName("source")] public string Source { get; init; } = string.Empty;

        [JsonPropertyName("level")] public string Level { get; init; } = string.Empty;

        [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    }
}