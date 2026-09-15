using System.Runtime.Loader;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins.Services;

internal sealed class PluginServiceRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Type, ServiceEntry> _services = [];

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

    public void UnregisterPlugin(string pluginId)
    {
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

    private sealed record ServiceEntry(string OwnerPluginId, object Instance)
    {
        public HashSet<string> ConsumerPluginIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class PluginServiceScope(PluginServiceRegistry registry, string pluginId) : IPluginServices, IDisposable
{
    private int _disposed;

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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            registry.UnregisterPlugin(pluginId);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}
