namespace ShiroBot.SDK.Plugin;

/// <summary>
/// 宿主运行状态快照，与 Dashboard 概览显示的信息相同。每次调用 <see cref="IBotContext.GetHostStatus"/> 返回新的快照。
/// </summary>
public sealed record HostStatus
{
    /// <summary>宿主产品版本，例如 1.0.2。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>宿主提供的 SDK ABI 版本，例如 1.3.0.0。</summary>
    public string SdkAbiVersion { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan Uptime { get; init; }

    public HostRuntimeInfo Runtime { get; init; } = new();

    /// <summary>已加载的插件数量。</summary>
    public int PluginCount { get; init; }

    /// <summary>已注册的平台 Model 包数量。</summary>
    public int ModelCount { get; init; }

    /// <summary>宿主启动以来收到的消息总数。</summary>
    public long MessageCount { get; init; }

    /// <summary>按本地时间每 2 小时一段的消息数，共 12 段，从 00:00 开始；统计自宿主启动。</summary>
    public IReadOnlyList<MessageFrequencyBucket> MessageFrequency { get; init; } = [];

    /// <summary>最近一小时内没有错误时为 true，与 Dashboard 的“正常 / 异常”一致。</summary>
    public bool Healthy { get; init; } = true;

    /// <summary>最近一小时内最新的错误；没有时为 null。</summary>
    public string? LatestError { get; init; }
}

/// <summary>宿主进程的运行环境。</summary>
public sealed record HostRuntimeInfo
{
    /// <summary>运行方式：docker、systemd 或 native。</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>操作系统描述，例如 "Alpine Linux v3.22" 或 "macOS 26.0.0"。</summary>
    public string OperatingSystem { get; init; } = string.Empty;

    /// <summary>进程架构：x64、arm64 等。</summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>.NET 运行时描述，例如 ".NET 10.0.0"。</summary>
    public string Framework { get; init; } = string.Empty;

    /// <summary>Docker 镜像标签；不是镜像运行时为 null。</summary>
    public string? ImageTag { get; init; }

    /// <summary>构建时间；未知时为 null。</summary>
    public DateTimeOffset? BuildTime { get; init; }

    /// <summary>进程工作集内存（字节）。</summary>
    public long MemoryBytes { get; init; }

    /// <summary>托管堆已分配内存（字节）。</summary>
    public long GcHeapBytes { get; init; }

    public int ProcessorCount { get; init; }
}

/// <summary>一个时间段内收到的消息数。</summary>
public sealed record MessageFrequencyBucket
{
    public TimeOnly Start { get; init; }

    /// <summary>不含的结束时间；最后一段为 00:00。</summary>
    public TimeOnly End { get; init; }

    public int Count { get; init; }
}
