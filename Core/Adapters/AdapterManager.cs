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
using ShiroBot.SDK.Config;
using ShiroBot.Components.Updates;
using System.Text.Json;

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
    private readonly HashSet<string> _restartRequiredErrors = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> ApplyConfigByIdAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry? entry;
            lock (_sync) entry = _entries.Values.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry?.Configurable is null || entry.Adapter is null) return false;
            var candidate = ConfigContext.LoadUntyped(entry.Adapter.Config, entry.Configurable.ConfigType);
            await ApplyConfigCandidateAsync(entry, candidate).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>The loaded adapter's assembly, or null when the adapter is not running.</summary>
    public Assembly? GetLoadedAssembly(string id)
    {
        lock (_sync)
            return _entries.Values.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, id, StringComparison.OrdinalIgnoreCase))?.Adapter?.GetType().Assembly;
    }

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

    /// <summary>Forget a removed adapter's stale lifecycle error after its package is deleted.</summary>
    public void ForgetRemovedAdapter(string id)
    {
        lock (_sync)
        {
            if (_entries.Values.Any(entry => string.Equals(entry.Metadata.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("无法清理仍在运行的 Adapter 状态。");
            _errors.Remove(id);
            _restartRequiredErrors.Remove(id);
        }
    }

    public IReadOnlyList<AdapterRuntimeSnapshot> GetSnapshot()
    {
        lock (_sync)
        {
            return _entries.Values.Where(entry => entry.Adapter is not null).Select(entry => new AdapterRuntimeSnapshot(
                    entry.Metadata.Id, entry.Metadata.Name, entry.Metadata.Version, entry.Adapter!.Platform,
                    entry.Metadata.Description, entry.AssemblyPath, true, null, false))
                .Concat(_errors.Where(error => !_entries.Values.Any(entry => string.Equals(entry.Metadata.Id, error.Key, StringComparison.OrdinalIgnoreCase)))
                    .Select(error => new AdapterRuntimeSnapshot(error.Key, error.Key, null, null, null, null, false, error.Value, _restartRequiredErrors.Contains(error.Key))))
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

    public Task LoadByIdAsync(string id, string assemblyPath, bool forceFreshImage = false) =>
        LoadOneAsync(assemblyPath, id, forceFreshImage);

    public async Task LoadOneAsync(string assemblyPath, string? expectedId = null, bool forceFreshImage = false)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(expectedId))
            {
                lock (_sync)
                {
                    if (_entries.Values.Any(item => string.Equals(item.Metadata.Id, expectedId, StringComparison.OrdinalIgnoreCase)))
                        return;
                }
            }
            await LoadCoreAsync(Path.GetFullPath(assemblyPath), expectedId, forceFreshImage: forceFreshImage).ConfigureAwait(false);
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
            AdapterEntry? entry;
            lock (_sync) entry = _entries.Values.FirstOrDefault(item => string.Equals(item.Metadata.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return;
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

    public Task StopAsync(string? assemblyPath = null) => StopCoreAsync(assemblyPath, waitForAssemblyRelease: true);

    /// <summary>Stops connections and subscriptions at process exit without waiting for collectible assemblies.</summary>
    public Task StopForShutdownAsync() => StopCoreAsync(null, waitForAssemblyRelease: false);

    private async Task StopCoreAsync(string? assemblyPath, bool waitForAssemblyRelease)
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
                await StopAndRemoveAsync(current, waitForAssemblyRelease).ConfigureAwait(false);
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
        try
        {
            await StopEntryAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            // StopEntryAsync can fail after the adapter has stopped while the runtime checks
            // collectible-context release. Do not leave a dead entry that blocks future retries.
            if (entry.Adapter is null)
            {
                lock (_sync) _entries.Remove(entry.AssemblyPath);
            }
        }

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

    private async Task StopAndRemoveAsync(AdapterEntry entry, bool waitForAssemblyRelease = true)
    {
        try
        {
            await StopEntryAsync(entry, waitForAssemblyRelease).ConfigureAwait(false);
        }
        finally
        {
            if (entry.Adapter is null)
            {
                lock (_sync) _entries.Remove(entry.AssemblyPath);
            }
        }
    }

    private async Task LoadCoreAsync(string adapterPath, string? expectedId = null, string? logicalAssemblyPath = null, bool forceFreshImage = false)
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
        // A failed new version can keep its image mapped through the exception stack. Loading the
        // restored file at that same path may reuse the failed image despite disk rollback.
        var loadAssemblyPath = forceFreshImage ? CreateReloadShadow(adapterPath) : adapterPath;
        var loader = new DllLoader<IBotAdapter>(collectible: true, shared: sharedAssemblies, dependencies: dependencies);
        IAsyncDisposable? subscription = null;
        IDisposable? configWatch = null;
        IBotAdapter? adapter = null;
        var registered = false;
        try
        {
            adapter = loader.Load(loadAssemblyPath);
            var metadata = adapter.GetType().GetCustomAttribute<BotAdapterAttribute>(inherit: false)
                ?? throw new InvalidOperationException($"Adapter 未声明 {nameof(BotAdapterAttribute)}。");
            lock (_sync)
            {
                if (_entries.Values.Any(entry => string.Equals(entry.Metadata.Id, metadata.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"适配器实例 {metadata.Id} 已加载，不能重复加载。");
            }

            var configDirectory = Path.GetDirectoryName(logicalAssemblyPath ?? adapterPath) ?? adapterRoot;
            adapter.Config = ConfigContext.ForAdapter(Path.Combine(configDirectory, "config.toml"));
            adapter.Logger = new ConsoleLogger($"[Adapter:{metadata.Id}]", logHub);
            var configurable = adapter as IConfigurableAdapter;
            object? initialConfig = null;
            if (configurable is not null)
            {
                initialConfig = await configurable.InitializeConfigAsync(adapter.Config).ConfigureAwait(false);
            }
            botContext.RegisterAdapter(adapter, metadata.Id);
            registered = true;
            subscription = eventBridge.Bridge(metadata.Id, adapter.Platform, adapter.Event, directMessageHandler);
            using (BotLog.BeginScope(adapter.Logger)) await adapter.StartAsync().ConfigureAwait(false);
            if (configurable is not null)
            {
                configWatch = ConfigContext.WatchUntyped(adapter.Config, configurable.ConfigType,
                    updated => _ = QueueConfigUpdateAsync(metadata.Id, updated));
            }
            var fullPath = Path.GetFullPath(logicalAssemblyPath ?? adapterPath);
            var shadowAssemblyPath = forceFreshImage ? loadAssemblyPath : CreateReloadShadow(adapterPath);
            lock (_sync)
            {
                _entries[fullPath] = new AdapterEntry(fullPath, adapter, loader, metadata, subscription!,
                    configWatch, configurable, initialConfig,
                    Path.GetDirectoryName(shadowAssemblyPath), shadowAssemblyPath);
                _errors.Remove(metadata.Id);
                _restartRequiredErrors.Remove(metadata.Id);
            }
            subscription = null; // Entry owns it now.
            configWatch = null;
            logHub.RegisterSource(metadata.Id, metadata.Description ?? $"{metadata.Name} Adapter logs", metadata.Name, HostLogHub.LogSourceKind.Adapter);
            runtimeState.RecordEvent($"{metadata.Name} Adapter loaded");
        }
        catch
        {
            if (subscription is not null) await subscription.DisposeAsync().ConfigureAwait(false);
            configWatch?.Dispose();
            if (registered && adapter is not null) botContext.UnregisterAdapter(adapter);
            if (adapter is not null)
            {
                try { await adapter.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch { }
            }
            loader.Unload();
            if (forceFreshImage) TryDeleteDirectory(Path.GetDirectoryName(loadAssemblyPath));
            throw;
        }
    }

    private async Task StopEntryAsync(AdapterEntry entry, bool waitForAssemblyRelease = true)
    {
        var adapter = entry.Adapter ?? throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已进入卸载状态。");
        var subscription = entry.EventSubscription ?? throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已进入卸载状态。");
        var loader = entry.Loader ?? throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已进入卸载状态。");

        var adapterId = entry.Metadata.Id;
        var adapterName = entry.Metadata.Name;
        var assemblyPath = entry.AssemblyPath;
        var references = new AdapterUnloadReferences(
            new WeakReference(adapter),
            new WeakReference(subscription),
            entry.ConfigWatch is null ? null : new WeakReference(entry.ConfigWatch),
            entry.Configurable is null ? null : new WeakReference(entry.Configurable),
            entry.CurrentConfig is null ? null : new WeakReference(entry.CurrentConfig));
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        RecordAdapterLifecycle(adapterId, "info", $"unload begin; assembly={assemblyPath}; adapter_ref={RuntimeHelpers.GetHashCode(adapter)}; subscription_ref={RuntimeHelpers.GetHashCode(subscription)}");

        try
        {
            using (BotLog.BeginScope(adapter.Logger))
            {
                await adapter.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Start unloading only after StopAsync succeeds so a failed stop remains retryable.
            RecordAdapterLifecycle(adapterId, "error", $"unload aborted in StopAsync after {startedAt.ElapsedMilliseconds} ms: {ex}");
            throw;
        }

        RecordAdapterLifecycle(adapterId, "info", $"StopAsync completed in {startedAt.ElapsedMilliseconds} ms");
        try
        {
            entry.ConfigWatch?.Dispose();
            RecordAdapterLifecycle(adapterId, "info", "config watcher disposed");
            await subscription.DisposeAsync().ConfigureAwait(false);
            RecordAdapterLifecycle(adapterId, "info", $"event subscription drained in {startedAt.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            RecordAdapterLifecycle(adapterId, "error", $"unload cleanup failed after {startedAt.ElapsedMilliseconds} ms: {ex}");
            throw;
        }

        botContext.UnregisterAdapter(adapter);
        RecordAdapterLifecycle(adapterId, "info", "adapter removed from BotContext");
        entry.ReleaseRuntimeReferences();
        RecordAdapterLifecycle(adapterId, "info", "AdapterEntry runtime references cleared");
        adapter = null;
        subscription = null;
        var weakReference = loader.BeginUnload();
        loader = null;
        if (!waitForAssemblyRelease)
        {
            RecordAdapterLifecycle(adapterId, "info", "adapter stopped for process exit; assembly collection will be left to process teardown");
            runtimeState.RecordEvent($"{adapterName} Adapter stopped");
            return;
        }

        RecordAdapterLifecycle(adapterId, "info", "collectible AssemblyLoadContext.Unload requested; waiting for collection");
        await Task.Delay(200).ConfigureAwait(false);
        if (!WaitForAdapterUnload(weakReference))
        {
            var survivors = references.GetAliveReferences();
            var diagnostic = $"Adapter ALC unload timed out after {startedAt.ElapsedMilliseconds} ms; " +
                             $"alc_alive={weakReference?.IsAlive == true}; surviving_refs=[{string.Join(", ", survivors)}]; " +
                             $"assembly={assemblyPath}";
            RecordAdapterLifecycle(adapterId, "error", diagnostic);
            var holder = survivors.Count > 0
                ? $"仍存活的宿主侧引用: {string.Join(", ", survivors)}"
                : "宿主侧没有发现残留引用，引用可能由组件自身持有（静态字段、计时器、未释放的连接等）";
            var pending = new ComponentUnloadPendingException(
                $"Adapter {adapterName} 已停止，但程序集仍被引用，重启宿主后才能完成卸载。{holder}。");
            RecordError(adapterId, pending);
            throw pending;
        }
        RecordAdapterLifecycle(adapterId, "info", $"Adapter ALC collected successfully after {startedAt.ElapsedMilliseconds} ms");
        runtimeState.RecordEvent($"{adapterName} Adapter unloaded");
    }

    private void RecordAdapterLifecycle(string adapterId, string level, string message)
    {
        logHub.Record("system", level, $"Adapter lifecycle [{adapterId}]: {message}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForAdapterUnload(WeakReference? weakReference) =>
        DllLoader<IBotAdapter>.WaitForUnload(weakReference);

    private async Task QueueConfigUpdateAsync(string adapterId, object candidate)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry? entry;
            lock (_sync) entry = _entries.Values.FirstOrDefault(item =>
                string.Equals(item.Metadata.Id, adapterId, StringComparison.OrdinalIgnoreCase));
            if (entry is not null) await ApplyConfigCandidateAsync(entry, candidate).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logHub.Record("system", "error", $"Adapter 配置热重载失败: {adapterId} - {ex.Message}");
            runtimeState.RecordEvent($"Adapter config reload failed: {adapterId} - {ex.Message}", "error");
        }
        finally { _gate.Release(); }
    }

    private static async Task ApplyConfigCandidateAsync(AdapterEntry entry, object candidate)
    {
        var configurable = entry.Configurable;
        var adapter = entry.Adapter;
        if (configurable is null || adapter is null) return;
        entry.CurrentConfig = await ApplyAdapterConfigAsync(adapter, configurable, entry.CurrentConfig, candidate)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a config candidate to a running adapter and returns the config now in effect.
    /// RestartComponent adapters are stopped, reconfigured and started; on failure the previous
    /// config is restored and the adapter restarted before the original error is rethrown.
    /// </summary>
    internal static async Task<object?> ApplyAdapterConfigAsync(
        IBotAdapter adapter,
        IConfigurableAdapter configurable,
        object? current,
        object candidate)
    {
        if (string.Equals(Fingerprint(candidate, configurable.ConfigType),
                Fingerprint(current, configurable.ConfigType), StringComparison.Ordinal)) return current;

        if (configurable.ApplyMode == ConfigApplyMode.RestartComponent)
        {
            await adapter.StopAsync().ConfigureAwait(false);
            try
            {
                await configurable.ApplyConfigAsync(candidate).ConfigureAwait(false);
                await adapter.StartAsync().ConfigureAwait(false);
            }
            catch
            {
                if (current is not null)
                {
                    await configurable.ApplyConfigAsync(current).ConfigureAwait(false);
                    await adapter.StartAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        else
        {
            await configurable.ApplyConfigAsync(candidate).ConfigureAwait(false);
        }

        return candidate;
    }

    private static string Fingerprint(object? config, Type configType)
    {
        try { return JsonSerializer.Serialize(config, configType); }
        catch { return RuntimeHelpers.GetHashCode(config ?? configType).ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    private void UpdateRuntimeState()
    {
        string[] names;
        lock (_sync) names = _entries.Values.Select(entry => entry.Metadata.Name).ToArray();
        runtimeState.SetAdapter(names.Length == 0 ? "none" : string.Join(", ", names), names.Length == 0 ? "not_loaded" : "connected");
    }

    private void RecordError(string id, Exception exception)
    {
        lock (_sync)
        {
            _errors[id] = exception.Message;
            if (exception is ComponentUnloadPendingException) _restartRequiredErrors.Add(id);
            else _restartRequiredErrors.Remove(id);
        }
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

    private sealed record AdapterUnloadReferences(
        WeakReference Adapter,
        WeakReference? Subscription,
        WeakReference? ConfigWatch,
        WeakReference? Configurable,
        WeakReference? CurrentConfig)
    {
        public IReadOnlyList<string> GetAliveReferences()
        {
            var alive = new List<string>();
            if (Adapter.IsAlive) alive.Add("adapter-instance");
            if (Subscription?.IsAlive == true) alive.Add("event-subscription");
            if (ConfigWatch?.IsAlive == true) alive.Add("config-watch");
            if (Configurable?.IsAlive == true) alive.Add("configurable-interface");
            if (CurrentConfig?.IsAlive == true) alive.Add("config-object");
            return alive;
        }
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
        IDisposable? configWatch = null,
        IConfigurableAdapter? configurable = null,
        object? currentConfig = null,
        string? shadowRoot = null,
        string? shadowAssemblyPath = null)
    {
        public string AssemblyPath { get; } = assemblyPath;
        public IBotAdapter? Adapter { get; private set; } = adapter;
        public DllLoader<IBotAdapter>? Loader { get; private set; } = loader;
        public BotAdapterAttribute Metadata { get; } = metadata;
        public IAsyncDisposable? EventSubscription { get; private set; } = eventSubscription;
        public IDisposable? ConfigWatch { get; private set; } = configWatch;
        public IConfigurableAdapter? Configurable { get; private set; } = configurable;
        public object? CurrentConfig { get; set; } = currentConfig;
        public string? ShadowRoot { get; } = shadowRoot;
        public string? ShadowAssemblyPath { get; } = shadowAssemblyPath;

        public void ReleaseRuntimeReferences()
        {
            Adapter = null;
            Loader = null;
            EventSubscription = null;
            // ConfigContext.Watch<T> captures the component's closed generic config type.
            // Retaining the disposed subscription while verifying ALC collection can pin that
            // type (and its collectible load context), preventing hot unload from completing.
            ConfigWatch = null;
            Configurable = null;
            CurrentConfig = null;
        }
    }
}

internal sealed record AdapterRuntimeSnapshot(
    string Id, string Name, string? Version, string? Platform, string? Description,
    string? AssemblyPath, bool Loaded, string? Error, bool RestartRequired);
