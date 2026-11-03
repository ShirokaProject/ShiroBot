using System.Text.Json.Nodes;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins.Services;

/// <summary>
/// 在插件内执行一段代码：计入该插件正在处理的调用（卸载时会等待），并使用插件的日志作用域。
/// 插件正在卸载时返回 false，不执行。
/// </summary>
internal delegate Task<bool> PluginInvoker(Func<Task> work);

/// <summary>
/// 一个插件实例在注册表里的身份。注销按实例匹配而不是按插件 ID：
/// 旧实例卸载超时后在后台收尾时，不能把同 ID 新实例注册的服务和订阅一起删掉。
/// </summary>
internal sealed class PluginInteropOwner(string pluginId)
{
    public string PluginId { get; } = pluginId;
}

/// <summary>
/// 按名称的服务和事件。消息在宿主里只以 UTF-8 JSON 字节存在，进出时各复制一份，
/// 双方都不会拿到对方创建的对象，空字节表示 JSON null。
/// </summary>
internal sealed class PluginServiceRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, NamedService> _named = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BusSubscription>> _topics = new(StringComparer.Ordinal);

    public IDisposable Provide(PluginInteropOwner owner, string name, PluginUtf8Handler handler,
        PluginInvoker invoke, CancellationToken ownerStopping, ReadOnlyMemory<byte> metadata = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(handler);
        if (!PluginJsonBytes.IsValid(metadata.Span))
            throw new ArgumentException($"Metadata of plugin service '{name}' is not valid UTF-8 JSON.", nameof(metadata));
        var entry = new NamedService(owner, handler, invoke, ownerStopping, metadata.ToArray());
        lock (_lock)
        {
            if (_named.TryGetValue(name, out var existing))
                throw new InvalidOperationException($"Plugin service '{name}' is already provided by {existing.Owner.PluginId}.");
            _named.Add(name, entry);
        }
        return new Registration(() =>
        {
            lock (_lock)
            {
                if (_named.TryGetValue(name, out var current) && ReferenceEquals(current, entry)) _named.Remove(name);
            }
        });
    }

    public bool IsAvailable(string name)
    {
        // 和 Find、CallAsync 一致：提供方开始卸载后不再算作可用。
        lock (_lock) return _named.TryGetValue(name, out var entry) && !entry.OwnerStopping.IsCancellationRequested;
    }

    public IReadOnlyList<PluginServiceInfo> Find(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        lock (_lock)
        {
            // 提供方开始卸载后不再列出，和 CallAsync 拒绝调用保持一致。
            return _named
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) && !pair.Value.OwnerStopping.IsCancellationRequested)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PluginServiceInfo(pair.Key, pair.Value.Owner.PluginId, PluginJsonBytes.ToNode(pair.Value.Metadata))
                {
                    MetadataUtf8 = pair.Value.Metadata.ToArray()
                })
                .ToArray();
        }
    }

    public async Task<ReadOnlyMemory<byte>> CallAsync(string name, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
    {
        NamedService? entry;
        lock (_lock) _named.TryGetValue(name, out entry);
        if (entry is null) throw new PluginServiceUnavailableException(name);
        if (!PluginJsonBytes.IsValid(request.Span))
            throw new ArgumentException($"Arguments for plugin service '{name}' are not valid UTF-8 JSON.", nameof(request));

        var input = request.ToArray();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.OwnerStopping);
        byte[] result = [];
        Exception? failure = null;
        var ran = await entry.Invoke(async () =>
        {
            try { result = (await entry.Handler(input, linked.Token).ConfigureAwait(false)).ToArray(); }
            catch (Exception ex) { failure = ex; }
        }).ConfigureAwait(false);

        if (!ran) throw new PluginServiceUnavailableException(name);
        if (failure is OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.OwnerStopping.IsCancellationRequested) throw new PluginServiceUnavailableException(name);
        }
        // 只传异常类型名和消息：调用方保存提供方的异常对象会拉住提供方的程序集。
        if (failure is not null) throw new PluginServiceException(name, failure.GetType().FullName ?? failure.GetType().Name, failure.Message);
        // 提供方的字节处理器可能返回任意字节，在这里拦下，调用方只会看到 PluginServiceException。
        if (!PluginJsonBytes.IsValid(result))
            throw new PluginServiceException(name, typeof(System.Text.Json.JsonException).FullName!, "The provider returned invalid UTF-8 JSON.");
        return result;
    }

    public IDisposable Subscribe(PluginInteropOwner owner, string topic, PluginUtf8EventHandler handler,
        PluginInvoker invoke, CancellationToken ownerStopping)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateName(topic, nameof(topic));
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new BusSubscription(owner, handler, invoke, ownerStopping);
        lock (_lock)
        {
            if (!_topics.TryGetValue(topic, out var subscribers)) _topics[topic] = subscribers = [];
            subscribers.Add(subscription);
        }
        return new Registration(() =>
        {
            lock (_lock) RemoveSubscription(topic, subscription);
        });
    }

    public async Task<int> EmitAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ValidateName(topic, nameof(topic));
        if (!PluginJsonBytes.IsValid(payload.Span))
            throw new ArgumentException($"Payload of plugin event '{topic}' is not valid UTF-8 JSON.", nameof(payload));
        BusSubscription[] subscribers;
        lock (_lock) subscribers = _topics.TryGetValue(topic, out var list) ? [.. list] : [];
        if (subscribers.Length == 0) return 0;

        var json = payload.ToArray();
        var delivered = await Task.WhenAll(subscribers.Select(async subscriber =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, subscriber.OwnerStopping);
            return await subscriber.Invoke(async () =>
            {
                try { await subscriber.Handler(json.ToArray(), linked.Token).ConfigureAwait(false); }
                catch (Exception ex) when (!linked.IsCancellationRequested)
                {
                    // 在订阅者的日志作用域内，记录到订阅者自己的日志。
                    BotLog.Warning($"处理插件事件 {topic} 失败: {ex.Message}");
                }
                catch (Exception) when (linked.IsCancellationRequested) { }
            }).ConfigureAwait(false);
        })).ConfigureAwait(false);
        return delivered.Count(ran => ran);
    }

    /// <summary>
    /// 插件实例开始卸载：立即停止接收按名称的调用和事件，正在执行的调用由插件的调用计数等待。
    /// 只移除这个实例注册的内容，同 ID 的新实例不受影响。
    /// </summary>
    public void DetachInterop(PluginInteropOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_lock)
        {
            foreach (var name in _named.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).Select(pair => pair.Key).ToArray())
                _named.Remove(name);
            foreach (var (topic, subscribers) in _topics.ToArray())
            {
                subscribers.RemoveAll(subscription => ReferenceEquals(subscription.Owner, owner));
                if (subscribers.Count == 0) _topics.Remove(topic);
            }
        }
    }

    private void RemoveSubscription(string topic, BusSubscription subscription)
    {
        if (!_topics.TryGetValue(topic, out var subscribers)) return;
        var index = subscribers.FindIndex(item => ReferenceEquals(item, subscription));
        if (index >= 0) subscribers.RemoveAt(index);
        if (subscribers.Count == 0) _topics.Remove(topic);
    }

    private static void ValidateName(string name, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameter);
        if (name.Length > 200 || name.Any(char.IsWhiteSpace))
            throw new ArgumentException("Names must be at most 200 characters without whitespace, e.g. \"weather.query\".", parameter);
    }

    private sealed class NamedService(PluginInteropOwner owner, PluginUtf8Handler handler,
        PluginInvoker invoke, CancellationToken ownerStopping, byte[] metadata)
    {
        public PluginInteropOwner Owner { get; } = owner;
        public PluginUtf8Handler Handler { get; } = handler;
        public PluginInvoker Invoke { get; } = invoke;
        public CancellationToken OwnerStopping { get; } = ownerStopping;
        public byte[] Metadata { get; } = metadata;
    }

    private sealed class BusSubscription(PluginInteropOwner owner, PluginUtf8EventHandler handler,
        PluginInvoker invoke, CancellationToken ownerStopping)
    {
        public PluginInteropOwner Owner { get; } = owner;
        public PluginUtf8EventHandler Handler { get; } = handler;
        public PluginInvoker Invoke { get; } = invoke;
        public CancellationToken OwnerStopping { get; } = ownerStopping;
    }

    private sealed class Registration(Action remove) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) remove();
        }
    }
}

/// <param name="owner">本插件实例在注册表里的身份，和同一实例的 <see cref="PluginEventBusScope"/> 共用。</param>
/// <param name="invoke">在本插件内执行按名称调用和事件处理；为 null 时直接执行（测试或旧用法）。</param>
/// <param name="lifetime">按名称提供的服务登记到插件生命周期，随卸载注销。</param>
internal sealed class PluginServiceScope(PluginServiceRegistry registry, PluginInteropOwner owner,
    PluginInvoker? invoke = null, PluginLifetimeScope? lifetime = null) : IPluginServices, IDisposable
{
    private int _disposed;
    private readonly PluginInvoker _invoke = invoke ?? (async work => { await work().ConfigureAwait(false); return true; });

    public PluginServiceScope(PluginServiceRegistry registry, string pluginId,
        PluginInvoker? invoke = null, PluginLifetimeScope? lifetime = null)
        : this(registry, new PluginInteropOwner(pluginId), invoke, lifetime)
    {
    }

    public PluginInteropOwner Owner => owner;

    public IDisposable Provide(string name, Func<JsonNode?, CancellationToken, Task<JsonNode?>> handler) =>
        Provide(name, handler, null);

    public IDisposable Provide(string name, Func<JsonNode?, CancellationToken, Task<JsonNode?>> handler, JsonNode? metadata)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return ProvideUtf8(name,
            async (request, ct) => PluginJsonBytes.FromNode(await handler(PluginJsonBytes.ToNode(request), ct).ConfigureAwait(false)),
            PluginJsonBytes.FromNode(metadata));
    }

    public IDisposable ProvideUtf8(string name, PluginUtf8Handler handler, ReadOnlyMemory<byte> metadata = default)
    {
        ThrowIfDisposed();
        var registration = registry.Provide(owner, name, handler, _invoke, lifetime?.Stopping ?? CancellationToken.None, metadata);
        lifetime?.Track(registration);
        return registration;
    }

    public async Task<JsonNode?> CallAsync(string name, JsonNode? arguments = null, CancellationToken cancellationToken = default) =>
        PluginJsonBytes.ToNode(await CallUtf8Async(name, PluginJsonBytes.FromNode(arguments), cancellationToken).ConfigureAwait(false));

    public Task<ReadOnlyMemory<byte>> CallUtf8Async(string name, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return registry.CallAsync(name, request, cancellationToken);
    }

    public bool IsAvailable(string name) => registry.IsAvailable(name);

    public IReadOnlyList<PluginServiceInfo> Find(string prefix) => registry.Find(prefix);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            registry.DetachInterop(owner);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}

internal sealed class PluginEventBusScope(PluginServiceRegistry registry, PluginInteropOwner owner,
    PluginInvoker invoke, PluginLifetimeScope lifetime) : IPluginEventBus
{
    public PluginEventBusScope(PluginServiceRegistry registry, string pluginId,
        PluginInvoker invoke, PluginLifetimeScope lifetime)
        : this(registry, new PluginInteropOwner(pluginId), invoke, lifetime)
    {
    }

    public PluginInteropOwner Owner => owner;

    public IDisposable On(string topic, Func<JsonNode?, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnUtf8(topic, (payload, ct) => handler(PluginJsonBytes.ToNode(payload), ct));
    }

    public IDisposable OnUtf8(string topic, PluginUtf8EventHandler handler) =>
        lifetime.Track(registry.Subscribe(owner, topic, handler, invoke, lifetime.Stopping));

    public Task<int> EmitAsync(string topic, JsonNode? payload = null, CancellationToken cancellationToken = default) =>
        EmitUtf8Async(topic, PluginJsonBytes.FromNode(payload), cancellationToken);

    public Task<int> EmitUtf8Async(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) =>
        registry.EmitAsync(topic, payload, cancellationToken);
}
