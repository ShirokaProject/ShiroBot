using System.Reflection;
using System.Runtime.CompilerServices;
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
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded
    {
        get { lock (_sync) return _entries.Count > 0; }
    }
    public IReadOnlyList<string> AssemblyPaths
    {
        get { lock (_sync) return _entries.Keys.ToArray(); }
    }

    public IReadOnlyList<string> LoadedIds
    {
        get { lock (_sync) return _entries.Values.Select(entry => entry.Metadata.Id).ToArray(); }
    }

    public IReadOnlyList<AdapterRuntimeSnapshot> GetSnapshot()
    {
        lock (_sync)
        {
            return _entries.Values.Where(entry => entry.Adapter is not null).Select(entry => new AdapterRuntimeSnapshot(
                    entry.Metadata.Id, entry.Metadata.Name, entry.Metadata.Version, entry.Adapter!.Platform,
                    entry.Metadata.Description, entry.AssemblyPath, true, null, false))
                .Concat(_errors.Where(error => !_entries.Values.Any(entry => string.Equals(entry.Metadata.Id, error.Key, StringComparison.OrdinalIgnoreCase)))
                    .Select(error => new AdapterRuntimeSnapshot(error.Key, error.Key, null, null, null, null, false, error.Value, false)))
                .ToArray();
        }
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
            platform = primary?.Adapter?.Platform,
            assembly_path = primary?.AssemblyPath,
            adapters = entries.Select(entry => new
            {
                id = entry.Metadata.Id,
                name = entry.Metadata.Name,
                version = entry.Metadata.Version,
                platform = entry.Adapter?.Platform,
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
                    RecordError(Path.GetFileNameWithoutExtension(path), ex);
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

    public Task LoadByIdAsync(string id, string assemblyPath) => LoadOneAsync(assemblyPath, id);

    public async Task LoadOneAsync(string assemblyPath, string? expectedId = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(Path.GetFullPath(assemblyPath), expectedId).ConfigureAwait(false);
            UpdateRuntimeState();
        }
        catch (Exception ex)
        {
            RecordError(expectedId ?? Path.GetFileNameWithoutExtension(assemblyPath), ex);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task ReloadByIdAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry entry;
            lock (_sync) entry = _entries.Values.FirstOrDefault(item => string.Equals(item.Metadata.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"未加载 Adapter: {id}");
            await ReloadEntryAsync(entry).ConfigureAwait(false);
            UpdateRuntimeState();
        }
        finally { _gate.Release(); }
    }

    public async Task StopByIdAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry entry;
            lock (_sync) entry = _entries.Values.FirstOrDefault(item => string.Equals(item.Metadata.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"未加载 Adapter: {id}");
            await StopAndRemoveAsync(entry).ConfigureAwait(false);
            UpdateRuntimeState();
        }
        finally { _gate.Release(); }
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
                lock (_sync) _entries.TryGetValue(path, out entry);
                if (entry is not null) await ReloadEntryAsync(entry).ConfigureAwait(false);
                else await LoadCoreAsync(path).ConfigureAwait(false);
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
                await StopAndRemoveAsync(current).ConfigureAwait(false);
            }
            UpdateRuntimeState();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReloadEntryAsync(AdapterEntry entry)
    {
        var shadowPath = entry.ShadowAssemblyPath ?? CreateReloadShadow(entry.AssemblyPath);
        await StopEntryAsync(entry).ConfigureAwait(false);
        lock (_sync) _entries.Remove(entry.AssemblyPath);
        try
        {
            await LoadCoreAsync(entry.AssemblyPath, entry.Metadata.Id).ConfigureAwait(false);
        }
        catch (Exception reloadError)
        {
            try
            {
                await LoadCoreAsync(shadowPath, entry.Metadata.Id, entry.AssemblyPath).ConfigureAwait(false);
                RecordError(entry.Metadata.Id, new InvalidOperationException($"Adapter reload failed; restored shadow copy: {reloadError.Message}"));
            }
            catch (Exception restoreError)
            {
                RecordError(entry.Metadata.Id, new InvalidOperationException($"Adapter reload and shadow restore failed: {reloadError.Message}; {restoreError.Message}"));
                throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 重载失败且旧版本恢复失败。", new AggregateException(reloadError, restoreError));
            }
        }
    }

    private async Task StopAndRemoveAsync(AdapterEntry entry)
    {
        try
        {
            await StopEntryAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            if (entry.Adapter is null)
            {
                lock (_sync) _entries.Remove(entry.AssemblyPath);
            }
        }
    }

    private async Task LoadCoreAsync(string adapterPath, string? expectedId = null, string? logicalAssemblyPath = null)
    {
        if (!File.Exists(adapterPath)) throw new FileNotFoundException("Adapter DLL 不存在。", adapterPath);

        var probeInfo = AdapterContractProbe.ReadMetadata(adapterPath)
            ?? throw new InvalidOperationException($"Adapter 未声明有效的 {nameof(BotAdapterAttribute)}。");
        if (!string.IsNullOrWhiteSpace(expectedId) && !string.Equals(expectedId, probeInfo.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Adapter ID 不匹配: 期望 {expectedId}，实际 {probeInfo.Id}。");
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
        IAsyncDisposable? subscription = null;
        IBotAdapter? adapter = null;
        var registered = false;
        try
        {
            adapter = loader.Load(adapterPath);
            var metadata = adapter.GetType().GetCustomAttribute<BotAdapterAttribute>(inherit: false)
                ?? throw new InvalidOperationException($"Adapter 未声明 {nameof(BotAdapterAttribute)}。");
            lock (_sync)
            {
                if (_entries.Values.Any(entry => string.Equals(entry.Adapter?.Platform, adapter.Platform, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"平台 {adapter.Platform} 已有 Adapter 加载，不能重复加载。");
            }

            adapter.Config = ConfigContext.ForAdapter(Path.Combine(Path.GetDirectoryName(adapterPath) ?? adapterRoot, "config.toml"));
            adapter.Logger = new ConsoleLogger($"[Adapter:{metadata.Id}]", logHub);
            subscription = eventBridge.Bridge(adapter.Platform, adapter.Event, directMessageHandler);
            using (BotLog.BeginScope(adapter.Logger)) await adapter.StartAsync().ConfigureAwait(false);
            var fullPath = Path.GetFullPath(logicalAssemblyPath ?? adapterPath);
            var shadowAssemblyPath = CreateReloadShadow(adapterPath);
            botContext.RegisterAdapter(adapter);
            registered = true;
            lock (_sync)
            {
                _entries[fullPath] = new AdapterEntry(fullPath, adapter, loader, metadata, subscription!, Path.GetDirectoryName(shadowAssemblyPath), shadowAssemblyPath);
                _errors.Remove(metadata.Id);
            }
            subscription = null; // Entry owns it now.
            logHub.RegisterSource(metadata.Id, metadata.Description ?? $"{metadata.Name} Adapter logs", metadata.Name);
            runtimeState.RecordEvent($"{metadata.Name} Adapter loaded");
        }
        catch
        {
            if (subscription is not null) await subscription.DisposeAsync().ConfigureAwait(false);
            if (registered && adapter is not null) botContext.UnregisterAdapter(adapter);
            if (adapter is not null)
            {
                try { await adapter.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch { }
            }
            loader.Unload();
            throw;
        }
    }

    private async Task StopEntryAsync(AdapterEntry entry)
    {
        var adapter = entry.Adapter;
        var subscription = entry.EventSubscription;
        var loader = entry.Loader;
        if (adapter is null || subscription is null || loader is null)
            throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已进入卸载状态。");

        try
        {
            using (BotLog.BeginScope(adapter.Logger))
            {
                await adapter.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
        catch
        {
            // Start unloading only after StopAsync succeeds so a failed stop remains retryable.
            throw;
        }

        await subscription.DisposeAsync().ConfigureAwait(false);
        botContext.UnregisterAdapter(adapter);
        entry.ReleaseRuntimeReferences();
        adapter = null;
        subscription = null;
        var weakReference = loader.BeginUnload();
        loader = null;
        await Task.Delay(200).ConfigureAwait(false);
        if (!WaitForAdapterUnload(weakReference))
        {
            RecordError(entry.Metadata.Id, new InvalidOperationException($"Adapter {entry.Metadata.Name} 已停止，但程序集仍被引用，需要重启宿主才能完成卸载。"));
            throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已停止，但程序集仍被引用，需要重启宿主才能完成卸载。");
        }
        runtimeState.RecordEvent($"{entry.Metadata.Name} Adapter unloaded");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForAdapterUnload(WeakReference? weakReference) =>
        DllLoader<IBotAdapter>.WaitForUnload(weakReference);

    private void UpdateRuntimeState()
    {
        string[] names;
        lock (_sync) names = _entries.Values.Select(entry => entry.Metadata.Name).ToArray();
        runtimeState.SetAdapter(names.Length == 0 ? "none" : string.Join(", ", names), names.Length == 0 ? "not_loaded" : "connected");
    }

    private void RecordError(string id, Exception exception)
    {
        lock (_sync) _errors[id] = exception.Message;
    }

    private static string CreateReloadShadow(string assemblyPath)
    {
        var sourceRoot = Path.GetDirectoryName(assemblyPath) ?? throw new InvalidOperationException("无法创建 Adapter 重载快照。");
        var shadowRoot = Path.Combine(Path.GetTempPath(), "ShiroBot", "adapter-reload", Guid.NewGuid().ToString("N"));
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(shadowRoot, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        return Path.Combine(shadowRoot, Path.GetRelativePath(sourceRoot, assemblyPath));
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class AdapterEntry(
        string assemblyPath,
        IBotAdapter adapter,
        DllLoader<IBotAdapter> loader,
        BotAdapterAttribute metadata,
        IAsyncDisposable eventSubscription,
        string? shadowRoot = null,
        string? shadowAssemblyPath = null)
    {
        public string AssemblyPath { get; } = assemblyPath;
        public IBotAdapter? Adapter { get; private set; } = adapter;
        public DllLoader<IBotAdapter>? Loader { get; private set; } = loader;
        public BotAdapterAttribute Metadata { get; } = metadata;
        public IAsyncDisposable? EventSubscription { get; private set; } = eventSubscription;
        public string? ShadowRoot { get; } = shadowRoot;
        public string? ShadowAssemblyPath { get; } = shadowAssemblyPath;

        public void ReleaseRuntimeReferences()
        {
            Adapter = null;
            Loader = null;
            EventSubscription = null;
        }
    }
}

internal sealed record AdapterRuntimeSnapshot(
    string Id, string Name, string? Version, string? Platform, string? Description,
    string? AssemblyPath, bool Loaded, string? Error, bool RestartRequired);
