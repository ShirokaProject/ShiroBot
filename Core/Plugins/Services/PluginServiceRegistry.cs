using System.Runtime.Loader;
using System.Text.Json.Nodes;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins.Services;

/// <summary>
/// 在插件内执行一段代码：计入该插件正在处理的调用（卸载时会等待），并使用插件的日志作用域。
/// 插件正在卸载时返回 false，不执行。
/// </summary>
internal delegate Task<bool> PluginInvoker(Func<Task> work);

internal sealed class PluginServiceRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Type, ServiceEntry> _services = [];
    private readonly Dictionary<string, NamedService> _named = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BusSubscription>> _topics = new(StringComparer.Ordinal);

    public void Register<TService>(string ownerPluginId, TService service)
        where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPluginId);
        ArgumentNullException.ThrowIfNull(service);

        var serviceType = typeof(TService);
        if (!serviceType.IsInterface && !serviceType.IsAbstract)
        {
            throw new InvalidOperationException(
                $"Plugin service contract {serviceType.FullName} must be an interface or abstract class.");
        }

        if (AssemblyLoadContext.GetLoadContext(serviceType.Assembly) != AssemblyLoadContext.Default)
        {
            throw new InvalidOperationException(
                $"Plugin service contract {serviceType.FullName} is not loaded in the Default AssemblyLoadContext. " +
                "Move the contract into a separate assembly and declare it in BotPluginAttribute.SharedAssemblies.");
        }

        if (!serviceType.IsInstanceOfType(service))
        {
            throw new ArgumentException(
                $"Service instance {service.GetType().FullName} does not implement {serviceType.FullName}.",
                nameof(service));
        }

        lock (_lock)
        {
            if (_services.TryGetValue(serviceType, out var existing))
            {
                throw new InvalidOperationException(
                    $"Plugin service {serviceType.FullName} is already registered by {existing.OwnerPluginId}.");
            }

            _services.Add(serviceType, new ServiceEntry(ownerPluginId, service));
        }
    }

    public object? GetService(string consumerPluginId, Type serviceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerPluginId);
        ArgumentNullException.ThrowIfNull(serviceType);

        lock (_lock)
        {
            if (!_services.TryGetValue(serviceType, out var entry))
            {
                return null;
            }

            if (!string.Equals(entry.OwnerPluginId, consumerPluginId, StringComparison.OrdinalIgnoreCase))
            {
                entry.ConsumerPluginIds.Add(consumerPluginId);
            }

            return entry.Instance;
        }
    }

    public IReadOnlyList<string> GetConsumers(string ownerPluginId)
    {
        lock (_lock)
        {
            return _services.Values
                .Where(entry => string.Equals(entry.OwnerPluginId, ownerPluginId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.ConsumerPluginIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IDisposable Provide(string ownerPluginId, string name, Func<JsonNode?, CancellationToken, Task<JsonNode?>> handler,
        PluginInvoker invoke, CancellationToken ownerStopping)
    {
        ValidateName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(handler);
        var entry = new NamedService(ownerPluginId, handler, invoke, ownerStopping);
        lock (_lock)
        {
            if (_named.TryGetValue(name, out var existing))
                throw new InvalidOperationException($"Plugin service '{name}' is already provided by {existing.OwnerPluginId}.");
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
        lock (_lock) return _named.ContainsKey(name);
    }

    public async Task<JsonNode?> CallAsync(string name, JsonNode? arguments, CancellationToken cancellationToken)
    {
        NamedService? entry;
        lock (_lock) _named.TryGetValue(name, out entry);
        if (entry is null) throw new PluginServiceUnavailableException(name);

        // 参数和返回值都按 JSON 文本复制，双方不会持有对方插件创建的对象。
        var input = Isolate(arguments);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.OwnerStopping);
        JsonNode? result = null;
        Exception? failure = null;
        var ran = await entry.Invoke(async () =>
        {
            try { result = Isolate(await entry.Handler(input, linked.Token).ConfigureAwait(false)); }
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
        return result;
    }

    public IDisposable Subscribe(string ownerPluginId, string topic, Func<JsonNode?, CancellationToken, Task> handler,
        PluginInvoker invoke, CancellationToken ownerStopping)
    {
        ValidateName(topic, nameof(topic));
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new BusSubscription(ownerPluginId, handler, invoke, ownerStopping);
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

    public async Task<int> EmitAsync(string topic, JsonNode? payload, CancellationToken cancellationToken)
    {
        ValidateName(topic, nameof(topic));
        BusSubscription[] subscribers;
        lock (_lock) subscribers = _topics.TryGetValue(topic, out var list) ? [.. list] : [];
        if (subscribers.Length == 0) return 0;

        var json = payload?.ToJsonString();
        var delivered = await Task.WhenAll(subscribers.Select(async subscriber =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, subscriber.OwnerStopping);
            return await subscriber.Invoke(async () =>
            {
                try { await subscriber.Handler(json is null ? null : JsonNode.Parse(json), linked.Token).ConfigureAwait(false); }
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

    /// <summary>插件开始卸载：立即停止接收按名称的调用和事件，正在执行的调用由插件的调用计数等待。</summary>
    public void DetachInterop(string pluginId)
    {
        lock (_lock)
        {
            foreach (var name in _named.Where(pair => SameOwner(pair.Value.OwnerPluginId, pluginId)).Select(pair => pair.Key).ToArray())
                _named.Remove(name);
            foreach (var (topic, subscribers) in _topics.ToArray())
            {
                subscribers.RemoveAll(subscription => SameOwner(subscription.OwnerPluginId, pluginId));
                if (subscribers.Count == 0) _topics.Remove(topic);
            }
        }
    }

    public void UnregisterPlugin(string pluginId)
    {
        DetachInterop(pluginId);
        lock (_lock)
        {
            foreach (var serviceType in _services
                         .Where(pair => string.Equals(pair.Value.OwnerPluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _services.Remove(serviceType);
            }

            foreach (var entry in _services.Values)
            {
                entry.ConsumerPluginIds.Remove(pluginId);
            }
        }
    }

    private void RemoveSubscription(string topic, BusSubscription subscription)
    {
        if (!_topics.TryGetValue(topic, out var subscribers)) return;
        subscribers.Remove(subscription);
        if (subscribers.Count == 0) _topics.Remove(topic);
    }

    private static bool SameOwner(string owner, string pluginId) =>
        string.Equals(owner, pluginId, StringComparison.OrdinalIgnoreCase);

    private static void ValidateName(string name, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameter);
        if (name.Length > 200 || name.Any(char.IsWhiteSpace))
            throw new ArgumentException("Names must be at most 200 characters without whitespace, e.g. \"weather.query\".", parameter);
    }

    private static JsonNode? Isolate(JsonNode? node) => node is null ? null : JsonNode.Parse(node.ToJsonString());

    private sealed record ServiceEntry(string OwnerPluginId, object Instance)
    {
        public HashSet<string> ConsumerPluginIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record NamedService(string OwnerPluginId, Func<JsonNode?, CancellationToken, Task<JsonNode?>> Handler,
        PluginInvoker Invoke, CancellationToken OwnerStopping);

    private sealed record BusSubscription(string OwnerPluginId, Func<JsonNode?, CancellationToken, Task> Handler,
        PluginInvoker Invoke, CancellationToken OwnerStopping);

    private sealed class Registration(Action remove) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) remove();
        }
    }
}

/// <param name="invoke">在本插件内执行按名称调用和事件处理；为 null 时直接执行（测试或旧用法）。</param>
/// <param name="lifetime">按名称提供的服务登记到插件生命周期，随卸载注销。</param>
internal sealed class PluginServiceScope(PluginServiceRegistry registry, string pluginId,
    PluginInvoker? invoke = null, PluginLifetimeScope? lifetime = null) : IPluginServices, IDisposable
{
    private int _disposed;
    private readonly PluginInvoker _invoke = invoke ?? (async work => { await work().ConfigureAwait(false); return true; });

    public void RegisterSingleton<TService>(TService service)
        where TService : class
    {
        ThrowIfDisposed();
        registry.Register(pluginId, service);
    }

    public object? GetService(Type serviceType)
    {
        ThrowIfDisposed();
        return registry.GetService(pluginId, serviceType);
    }

    public TService? GetService<TService>()
        where TService : class => GetService(typeof(TService)) as TService;

    public TService GetRequiredService<TService>()
        where TService : class => GetService<TService>()
            ?? throw new InvalidOperationException($"Required plugin service {typeof(TService).FullName} is not registered.");

    public IDisposable Provide(string name, Func<JsonNode?, CancellationToken, Task<JsonNode?>> handler)
    {
        ThrowIfDisposed();
        var registration = registry.Provide(pluginId, name, handler, _invoke, lifetime?.Stopping ?? CancellationToken.None);
        lifetime?.Track(registration);
        return registration;
    }

    public Task<JsonNode?> CallAsync(string name, JsonNode? arguments = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return registry.CallAsync(name, arguments, cancellationToken);
    }

    public bool IsAvailable(string name) => registry.IsAvailable(name);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            registry.UnregisterPlugin(pluginId);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}

internal sealed class PluginEventBusScope(PluginServiceRegistry registry, string pluginId,
    PluginInvoker invoke, PluginLifetimeScope lifetime) : IPluginEventBus
{
    public IDisposable On(string topic, Func<JsonNode?, CancellationToken, Task> handler) =>
        lifetime.Track(registry.Subscribe(pluginId, topic, handler, invoke, lifetime.Stopping));

    public Task<int> EmitAsync(string topic, JsonNode? payload = null, CancellationToken cancellationToken = default) =>
        registry.EmitAsync(topic, payload, cancellationToken);
}
