using System.Reflection;
using System.Text.Json.Serialization;

namespace ShiroBot.Hosting.Runtime;

internal sealed class HostRuntimeState(DateTimeOffset startedAt)
{
    private const int BucketHours = 2;
    private const int BucketCount = 24 / BucketHours;
    private const int MaxEvents = 20;

    private readonly Lock _lock = new();
    private readonly int[] _messageBuckets = new int[BucketCount];
    private readonly Queue<RuntimeEvent> _events = new();
    private long _messageCount;
    private RuntimeEvent? _latestError;

    private DateTimeOffset StartedAt { get; } = startedAt;
    private string BotVersion { get; } = GetBotVersion();
    private int PluginsCount { get; set; }
    private int ModelsCount { get; set; }
    private string Adapter { get; set; } = "unknown";
    private string AdapterStatus { get; set; } = "disconnected";

    public void SetAdapter(string adapterName, string status)
    {
        lock (_lock)
        {
            Adapter = adapterName;
            AdapterStatus = status;
        }
    }

    public void SetPluginsCount(int count)
    {
        lock (_lock)
        {
            PluginsCount = count;
        }
    }

    public void SetModelsCount(int count)
    {
        lock (_lock)
        {
            ModelsCount = count;
        }
    }

    public void RecordIncomingMessage(DateTimeOffset? at = null)
    {
        var localTime = (at ?? DateTimeOffset.Now).LocalDateTime;
        var bucket = Math.Clamp(localTime.Hour / BucketHours, 0, BucketCount - 1);

        lock (_lock)
        {
            _messageCount++;
            _messageBuckets[bucket]++;
        }
    }

    public void RecordEvent(string message, string level = "info", DateTimeOffset? at = null)
    {
        var runtimeEvent = new RuntimeEvent
        {
            Message = message,
            Time = (at ?? DateTimeOffset.Now).ToString("HH:mm"),
            Level = level
        };

        lock (_lock)
        {
            _events.Enqueue(runtimeEvent);
            while (_events.Count > MaxEvents)
            {
                _events.Dequeue();
            }

            if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
            {
                _latestError = runtimeEvent;
            }
        }
    }

    public object CreateOverview()
    {
        lock (_lock)
        {
            var events = _events.Reverse().ToArray();
            return new
            {
                bot_version = BotVersion,
                uptime_seconds = (long)(DateTimeOffset.UtcNow - StartedAt).TotalSeconds,
                plugins_count = PluginsCount,
                models_count = ModelsCount,
                adapter = Adapter,
                adapter_status = AdapterStatus,
                message_count = _messageCount,
                message_freq = CreateMessageFrequencySnapshot(),
                health_status = _latestError is null ? "正常" : "异常",
                latest_error = _latestError,
                events
            };
        }
    }

    private object[] CreateMessageFrequencySnapshot()
    {
        var result = new object[BucketCount];
        for (var i = 0; i < BucketCount; i++)
        {
            var startHour = i * BucketHours;
            var endHour = startHour + BucketHours;
            result[i] = new
            {
                start_time = $"{startHour:00}:00",
                end_time = $"{endHour:00}:00",
                count = _messageBuckets[i]
            };
        }

        return result;
    }

    private static string GetBotVersion()
    {
        var assembly = typeof(Program).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.9.0";
    }

    private sealed class RuntimeEvent
    {
        [JsonPropertyName("message")]
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("time")]
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public string Time { get; init; } = string.Empty;

        [JsonPropertyName("level")]
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public string Level { get; init; } = string.Empty;
    }
}
