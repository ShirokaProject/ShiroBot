using System.Reflection;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins.Loading;
using ShiroBot.Adapters.Compatibility;
using ShiroBot.Plugins.Compatibility;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;

namespace ShiroBot.Adapters;

internal sealed class AdapterManager(
    string adapterRoot,
    SharedAssemblyResolver sharedAssemblies,
    ModelPackageRegistry modelPackages,
    BotContext botContext,
    AdapterEventBridge eventBridge,
    HostRuntimeState runtimeState,
    HostLogHub logHub,
    Func<SDK.Models.MessageEvent, Task> directMessageHandler)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly Dictionary<string, AdapterEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded
    {
        get { lock (_sync) return _entries.Count > 0; }
    }
    public IReadOnlyList<string> AssemblyPaths
    {
        get { lock (_sync) return _entries.Keys.ToArray(); }
    }

    public object CreateStatus()
    {
        AdapterEntry[] entries;
        lock (_sync) entries = _entries.Values.ToArray();
        var primary = entries.FirstOrDefault();
        return new
        {
            loaded = entries.Length > 0,
            id = primary?.Metadata.Id,
            name = primary?.Metadata.Name,
            version = primary?.Metadata.Version,
            platform = primary?.Adapter.Platform,
            assembly_path = primary?.AssemblyPath,
            adapters = entries.Select(entry => new
            {
                id = entry.Metadata.Id,
                name = entry.Metadata.Name,
                version = entry.Metadata.Version,
                platform = entry.Adapter.Platform,
                assembly_path = entry.AssemblyPath
            }).ToArray()
        };
    }

    public async Task LoadAsync(IEnumerable<string> assemblyPaths)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var path in assemblyPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                lock (_sync)
                {
                    if (_entries.ContainsKey(path)) continue;
                }
                try
                {
                    await LoadCoreAsync(path).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logHub.Record("system", "error", $"Adapter 加载失败: {Path.GetFileName(path)} - {ex.Message}");
                    runtimeState.RecordEvent($"Adapter load failed: {Path.GetFileName(path)} - {ex.Message}", "error");
                }
            }
            UpdateRuntimeState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReloadAsync(string? assemblyPath = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var paths = assemblyPath is null
                ? AssemblyPaths.ToArray()
                : [Path.GetFullPath(assemblyPath)];
            if (paths.Length == 0 && assemblyPath is null)
                throw new InvalidOperationException("当前未加载 Adapter，必须提供 assemblyPath。");

            foreach (var path in paths)
            {
                AdapterEntry? entry;
                lock (_sync) _entries.Remove(path, out entry);
                if (entry is not null) await StopEntryAsync(entry).ConfigureAwait(false);
                await LoadCoreAsync(path).ConfigureAwait(false);
            }
            UpdateRuntimeState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(string? assemblyPath = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry[] entries;
            lock (_sync)
            {
                entries = assemblyPath is null
                    ? _entries.Values.ToArray()
                    : _entries.TryGetValue(Path.GetFullPath(assemblyPath), out var entry) ? [entry] : [];
            }
            foreach (var current in entries)
            {
                lock (_sync) _entries.Remove(current.AssemblyPath);
                await StopEntryAsync(current).ConfigureAwait(false);
            }
            UpdateRuntimeState();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadCoreAsync(string adapterPath)
    {
        if (!File.Exists(adapterPath)) throw new FileNotFoundException("Adapter DLL 不存在。", adapterPath);

        var probeInfo = AdapterContractProbe.ReadMetadata(adapterPath)
            ?? throw new InvalidOperationException($"Adapter 未声明有效的 {nameof(BotAdapterAttribute)}。");
        ComponentApiCompatibility.EnsureCompatible(
            "Adapter",
            probeInfo.Id,
            probeInfo.MinimumApiVersion,
            probeInfo.MaximumApiVersion);

        foreach (var contractName in probeInfo.SharedAssemblies)
        {
            if (modelPackages.ContainsAssembly(contractName)) continue;
            var contractPath = Path.Combine(Path.GetDirectoryName(adapterPath) ?? adapterRoot, contractName + ".dll");
            if (!File.Exists(contractPath))
                throw new FileNotFoundException($"Adapter 声明的共享程序集 {contractName}.dll 不存在。", contractPath);
            sharedAssemblies.RegisterDefaultAssembly(contractPath);
        }

        modelPackages.ValidateDependencies(adapterPath);
        var dependencies = await PluginRuntimeDependencyManager.PrepareAsync(
            adapterPath,
            Path.GetDirectoryName(adapterPath) ?? adapterRoot).ConfigureAwait(false);
        var loader = new DllLoader<IBotAdapter>(collectible: true, shared: sharedAssemblies, dependencies: dependencies);
        var adapter = loader.Load(adapterPath);
        var metadata = adapter.GetType().GetCustomAttribute<BotAdapterAttribute>(inherit: false)
            ?? throw new InvalidOperationException($"Adapter 未声明 {nameof(BotAdapterAttribute)}。");
        lock (_sync)
        {
            if (_entries.Values.Any(entry => string.Equals(entry.Adapter.Platform, adapter.Platform, StringComparison.OrdinalIgnoreCase)))
            {
                loader.Unload();
                throw new InvalidOperationException($"平台 {adapter.Platform} 已有 Adapter 加载，不能重复加载。");
            }
        }

        adapter.Config = ConfigContext.ForAdapter(Path.Combine(Path.GetDirectoryName(adapterPath) ?? adapterRoot, "config.toml"));
        adapter.Logger = new ConsoleLogger($"[Adapter:{metadata.Id}]", logHub);
        var subscription = eventBridge.Bridge(adapter.Platform, adapter.Event, directMessageHandler);
        try
        {
            using (BotLog.BeginScope(adapter.Logger)) await adapter.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
            loader.Unload();
            throw;
        }

        var fullPath = Path.GetFullPath(adapterPath);
        lock (_sync) _entries[fullPath] = new AdapterEntry(fullPath, adapter, loader, metadata, subscription);
        botContext.RegisterAdapter(adapter);
        logHub.RegisterSource(metadata.Id, metadata.Description ?? $"{metadata.Name} Adapter logs", metadata.Name);
        runtimeState.RecordEvent($"{metadata.Name} Adapter loaded");
    }

    private async Task StopEntryAsync(AdapterEntry entry)
    {
        await entry.EventSubscription.DisposeAsync().ConfigureAwait(false);
        botContext.UnregisterAdapter(entry.Adapter);
        using (BotLog.BeginScope(entry.Adapter.Logger))
        {
            await entry.Adapter.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        var weakReference = entry.Loader.BeginUnload();
        await Task.Delay(200).ConfigureAwait(false);
        if (!DllLoader<IBotAdapter>.WaitForUnload(weakReference))
            throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已停止，但程序集仍被引用，无法完成热卸载。");
        runtimeState.RecordEvent($"{entry.Metadata.Name} Adapter unloaded");
    }

    private void UpdateRuntimeState()
    {
        string[] names;
        lock (_sync) names = _entries.Values.Select(entry => entry.Metadata.Name).ToArray();
        runtimeState.SetAdapter(names.Length == 0 ? "none" : string.Join(", ", names), names.Length == 0 ? "not_loaded" : "connected");
    }

    private sealed record AdapterEntry(
        string AssemblyPath,
        IBotAdapter Adapter,
        DllLoader<IBotAdapter> Loader,
        BotAdapterAttribute Metadata,
        IAsyncDisposable EventSubscription);
}
