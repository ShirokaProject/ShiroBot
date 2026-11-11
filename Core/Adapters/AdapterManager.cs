using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ShiroBot.Adapters.Compatibility;
using ShiroBot.Components.Updates;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;

namespace ShiroBot.Adapters;

internal sealed class AdapterManager(
    string adapterRoot,
    SharedAssemblyResolver sharedAssemblies,
    ModelPackageRegistry modelPackages,
    BotContext botContext,
    AdapterEventBridge eventBridge,
    HostRuntimeState runtimeState,
    HostLogHub logHub)
{
    private readonly Dictionary<string, AdapterEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _restartRequiredErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();

    public bool IsLoaded
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count > 0;
            }
        }
    }

    public IReadOnlyList<string> AssemblyPaths
    {
        get
        {
            lock (_sync)
            {
                return
                [
                    .. _entries.Values.Select(entry => entry.AssemblyPath).Distinct(StringComparer.OrdinalIgnoreCase)
                ];
            }
        }
    }

    public IReadOnlyList<string> LoadedIds
    {
        get
        {
            lock (_sync)
            {
                return [.. _entries.Values.Select(entry => entry.Id)];
            }
        }
    }

    public async Task<bool> ApplyConfigByIdAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry? entry;
            lock (_sync)
            {
                entry = _entries.Values.FirstOrDefault(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            }

            if (entry?.Configurable is null || entry.Adapter is null) return false;
            var candidate = ConfigContext.LoadUntyped(entry.Adapter.Config, entry.Configurable.ConfigType);
            await ApplyConfigCandidateAsync(entry, candidate).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The loaded adapter's assembly, or null when the adapter is not running.</summary>
    public Assembly? GetLoadedAssembly(string id)
    {
        lock (_sync)
        {
            return _entries.Values.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))?.Adapter?.GetType().Assembly;
        }
    }

    /// <summary>Forget a removed adapter's stale lifecycle error after its package is deleted.</summary>
    public void ForgetRemovedAdapter(string id)
    {
        lock (_sync)
        {
            if (_entries.Values.Any(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("无法清理仍在运行的 Adapter 状态。");
            _errors.Remove(id);
            _restartRequiredErrors.Remove(id);
        }
    }

    public IReadOnlyList<AdapterRuntimeSnapshot> GetSnapshot()
    {
        lock (_sync)
        {
            return
            [
                .. _entries.Values.Where(entry => entry.Adapter is not null).Select(entry => new AdapterRuntimeSnapshot(
                        entry.Id, entry.Name, entry.Metadata.Version, entry.Adapter!.Platform,
                        entry.Metadata.Description, entry.AssemblyPath, true, null, false))
                    .Concat(_errors.Where(error => !_entries.Values.Any(entry =>
                            string.Equals(entry.Id, error.Key, StringComparison.OrdinalIgnoreCase)))
                        .Select(error => new AdapterRuntimeSnapshot(error.Key, error.Key, null, null, null, null, false,
                            error.Value, _restartRequiredErrors.Contains(error.Key))))
            ];
        }
    }

    public object CreateStatus()
    {
        AdapterEntry[] entries;
        lock (_sync)
        {
            entries = [.. _entries.Values];
        }

        var primary = entries.FirstOrDefault();
        return new
        {
            loaded = entries.Length > 0,
            id = primary?.Id,
            name = primary?.Name,
            version = primary?.Metadata.Version,
            platform = primary?.Adapter?.Platform,
            assembly_path = primary?.AssemblyPath,
            adapters = entries.Select(entry => new
            {
                id = entry.Id,
                name = entry.Name,
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
                    if (_entries.Values.Any(entry =>
                            string.Equals(entry.AssemblyPath, path, StringComparison.OrdinalIgnoreCase))) continue;
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

    public Task LoadByIdAsync(string id, string assemblyPath)
    {
        return LoadOneAsync(assemblyPath, id);
    }

    public async Task LoadInstanceAsync(InstalledAdapterInstance instance)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_entries.ContainsKey(instance.Id)) return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(instance.ConfigPath)!);
            await LoadCoreAsync(instance.AssemblyPath, instance.PackageId,
                    instanceId: instance.Id, configPath: instance.ConfigPath, instanceName: instance.Name)
                .ConfigureAwait(false);
            UpdateRuntimeState();
        }
        catch (Exception ex)
        {
            RecordError(instance.Id, ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LoadOneAsync(string assemblyPath, string? expectedId = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(expectedId))
                lock (_sync)
                {
                    if (_entries.Values.Any(item =>
                            string.Equals(item.Id, expectedId, StringComparison.OrdinalIgnoreCase)))
                        return;
                }

            await LoadCoreAsync(Path.GetFullPath(assemblyPath), expectedId).ConfigureAwait(false);
            UpdateRuntimeState();
        }
        catch (Exception ex)
        {
            RecordError(expectedId ?? Path.GetFileNameWithoutExtension(assemblyPath), ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Creates an instance. Adding one turns an implicit single-instance config into [[instances]], so a running
    ///     implicit instance (which reads the file root) restarts to read its own section.
    /// </summary>
    public async Task<InstalledAdapterInstance> CreateInstanceAsync(AdapterPackageManager packages, string packageId,
        string id, string? name)
    {
        var implicitRunning = packages.ListInstances().FirstOrDefault(item => item.Implicit &&
                                                                              string.Equals(item.PackageId, packageId,
                                                                                  StringComparison.OrdinalIgnoreCase) &&
                                                                              LoadedIds.Contains(item.Id,
                                                                                  StringComparer.OrdinalIgnoreCase));
        if (implicitRunning is not null) await StopByIdAsync(implicitRunning.Id).ConfigureAwait(false);
        try
        {
            return packages.CreateInstance(packageId, id, name);
        }
        finally
        {
            if (implicitRunning is not null && packages.GetInstance(implicitRunning.Id) is { } restarted)
                await LoadInstanceAsync(restarted).ConfigureAwait(false);
        }
    }

    public async Task ReloadByIdAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry entry;
            lock (_sync)
            {
                entry = _entries.Values.FirstOrDefault(item =>
                            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"未加载 Adapter: {id}");
            }

            await ReloadEntryAsync(entry).ConfigureAwait(false);
            UpdateRuntimeState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopByIdAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry? entry;
            lock (_sync)
            {
                entry = _entries.Values.FirstOrDefault(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            }

            if (entry is null) return;
            await StopAndRemoveAsync(entry).ConfigureAwait(false);
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
                AdapterEntry[] entries;
                lock (_sync)
                {
                    entries =
                    [
                        .. _entries.Values.Where(entry =>
                            string.Equals(entry.AssemblyPath, path, StringComparison.OrdinalIgnoreCase))
                    ];
                }

                if (entries.Length == 0) await LoadCoreAsync(path).ConfigureAwait(false);
                else
                    foreach (var entry in entries)
                        await ReloadEntryAsync(entry).ConfigureAwait(false);
            }

            UpdateRuntimeState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync(string? assemblyPath = null)
    {
        return StopCoreAsync(assemblyPath, true);
    }

    /// <summary>Stops connections and subscriptions at process exit without waiting for collectible assemblies.</summary>
    public Task StopForShutdownAsync()
    {
        return StopCoreAsync(null, false);
    }

    private async Task StopCoreAsync(string? assemblyPath, bool waitForAssemblyRelease)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry[] entries;
            lock (_sync)
            {
                entries = assemblyPath is null
                    ? [.. _entries.Values]
                    :
                    [
                        .. _entries.Values.Where(entry => string.Equals(entry.AssemblyPath,
                            Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase))
                    ];
            }

            foreach (var current in entries)
                await StopAndRemoveAsync(current, waitForAssemblyRelease).ConfigureAwait(false);
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
                lock (_sync)
                {
                    _entries.Remove(entry.Id);
                }
        }

        try
        {
            await LoadCoreAsync(entry.AssemblyPath, entry.Metadata.Id, instanceId: entry.Id,
                configPath: entry.ConfigPath, instanceName: entry.Name).ConfigureAwait(false);
        }
        catch (Exception reloadError)
        {
            try
            {
                await LoadCoreAsync(shadowPath, entry.Metadata.Id, entry.AssemblyPath, entry.Id, entry.ConfigPath,
                    entry.Name).ConfigureAwait(false);
                RecordError(entry.Id,
                    new InvalidOperationException(
                        $"Adapter reload failed; restored shadow copy: {reloadError.Message}"));
            }
            catch (Exception restoreError)
            {
                RecordError(entry.Id,
                    new InvalidOperationException(
                        $"Adapter reload and shadow restore failed: {reloadError.Message}; {restoreError.Message}"));
                throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 重载失败且旧版本恢复失败。",
                    new AggregateException(reloadError, restoreError));
            }
        }
        finally
        {
            // A restored instance owns a fresh shadow; the previous one is no longer needed.
            if (Environment.GetEnvironmentVariable("SHIROBOT_LIFECYCLE_DIAGNOSTICS") == "true")
                Console.WriteLine($"Adapter reload probe: deleting old shadow {entry.ShadowRoot}");
            TryDeleteDirectory(entry.ShadowRoot);
            if (Environment.GetEnvironmentVariable("SHIROBOT_LIFECYCLE_DIAGNOSTICS") == "true")
                Console.WriteLine("Adapter reload probe: old shadow deleted");
        }
    }

    private async Task StopAndRemoveAsync(AdapterEntry entry, bool waitForAssemblyRelease = true)
    {
        try
        {
            await StopEntryAsync(entry, waitForAssemblyRelease).ConfigureAwait(false);
            if (waitForAssemblyRelease) TryDeleteDirectory(entry.ShadowRoot);
        }
        finally
        {
            if (entry.Adapter is null)
                lock (_sync)
                {
                    _entries.Remove(entry.Id);
                }
        }
    }

    private async Task LoadCoreAsync(string adapterPath, string? expectedId = null, string? logicalAssemblyPath = null,
        string? instanceId = null, string? configPath = null, string? instanceName = null)
    {
        TraceLoad("begin");
        if (!File.Exists(adapterPath)) throw new FileNotFoundException("Adapter DLL 不存在。", adapterPath);

        TraceLoad("metadata");
        var probeInfo = AdapterContractProbe.ReadMetadata(adapterPath)
                        ?? throw new InvalidOperationException($"Adapter 未声明有效的 {nameof(BotAdapterAttribute)}。");
        if (!string.IsNullOrWhiteSpace(expectedId) &&
            !string.Equals(expectedId, probeInfo.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Adapter ID 不匹配: 期望 {expectedId}，实际 {probeInfo.Id}。");

        // A stopped but still referenced instance must not get a second live replacement.
        // Keep this guard before dependency setup, DLL activation and StartAsync.
        instanceId ??= probeInfo.Id;
        lock (_sync)
        {
            if (_restartRequiredErrors.Contains(instanceId))
                throw new ComponentUnloadPendingException($"Adapter 实例 {instanceId} 的旧程序集尚未释放，请重启宿主后再启动或替换。");
        }

        foreach (var contractName in probeInfo.SharedAssemblies)
        {
            if (modelPackages.ContainsAssembly(contractName)) continue;
            var contractPath = Path.Combine(Path.GetDirectoryName(adapterPath) ?? adapterRoot, contractName + ".dll");
            if (!File.Exists(contractPath))
                throw new FileNotFoundException($"Adapter 声明的共享程序集 {contractName}.dll 不存在。", contractPath);
            sharedAssemblies.RegisterDefaultAssembly(contractPath);
        }

        TraceLoad("dependencies");
        modelPackages.ValidateDependencies(adapterPath);
        var dependencies = await PluginRuntimeDependencyManager.PrepareAsync(
            adapterPath,
            Path.GetDirectoryName(adapterPath) ?? adapterRoot).ConfigureAwait(false);
        // Never map the installed DLL: an in-place file replacement can corrupt its lazy-loaded IL
        // before StopAsync is JIT-compiled. Each instance maps an independent, immutable copy.
        // Fresh paths also prevent rollback from reusing a failed version's mapped image.
        TraceLoad("shadow copy");
        var loadAssemblyPath = CreateReloadShadow(adapterPath);
        var loader = new DllLoader<IBotAdapter>(true, sharedAssemblies, dependencies);
        IAsyncDisposable? subscription = null;
        IDisposable? configWatch = null;
        IBotAdapter? adapter = null;
        var registered = false;
        try
        {
            TraceLoad("DLL activation");
            adapter = loader.Load(loadAssemblyPath);
            var metadata = adapter.GetType().GetCustomAttribute<BotAdapterAttribute>(false)
                           ?? throw new InvalidOperationException($"Adapter 未声明 {nameof(BotAdapterAttribute)}。");
            lock (_sync)
            {
                if (_entries.Values.Any(entry =>
                        string.Equals(entry.Id, instanceId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"适配器实例 {instanceId} 已加载，不能重复加载。");
            }

            var configDirectory = Path.GetDirectoryName(logicalAssemblyPath ?? adapterPath) ?? adapterRoot;
            configPath ??= Path.Combine(configDirectory, "config.toml");
            adapter.Config = ConfigContext.ForAdapter(configPath, instanceId);
            adapter.Logger = new ConsoleLogger($"[Adapter:{instanceId}]", logHub);
            var configurable = adapter as IConfigurableAdapter;
            object? initialConfig = null;
            if (configurable is not null)
            {
                TraceLoad("initial config");
                initialConfig = await configurable.InitializeConfigAsync(adapter.Config).ConfigureAwait(false);
            }

            TraceLoad("instance registration");
            botContext.RegisterAdapter(adapter, instanceId, instanceName ?? metadata.Name);
            registered = true;
            subscription = eventBridge.Bridge(instanceId, adapter.Platform, adapter.Event);
            TraceLoad("start");
            using (BotLog.BeginScope(adapter.Logger))
            {
                await StartAdapterAsync(adapter).ConfigureAwait(false);
            }

            if (configurable is not null)
            {
                TraceLoad("watch config");
                configWatch = ConfigContext.WatchUntyped(adapter.Config, configurable.ConfigType,
                    updated => _ = QueueConfigUpdateAsync(instanceId, updated));
            }

            var fullPath = Path.GetFullPath(logicalAssemblyPath ?? adapterPath);
            lock (_sync)
            {
                _entries[instanceId] = new AdapterEntry(instanceId, configPath, instanceName ?? metadata.Name, fullPath,
                    adapter, loader, metadata, subscription,
                    configWatch, configurable, initialConfig,
                    Path.GetDirectoryName(loadAssemblyPath), loadAssemblyPath);
                _errors.Remove(instanceId);
                _restartRequiredErrors.Remove(instanceId);
            }

            // Release references held by the async state machine before collectible-assembly or memory-retention checks.
            // ReSharper disable once RedundantAssignment
            subscription = null; // Entry owns it now.
            configWatch = null;
            TraceLoad("log registration");
            // Keep the instance ID as the routing key; show its configured name above the adapter type.
            logHub.RegisterSource(instanceId, metadata.Name,
                string.IsNullOrWhiteSpace(instanceName) ? instanceId : instanceName, HostLogHub.LogSourceKind.Adapter);
            runtimeState.RecordEvent($"{metadata.Name} Adapter loaded");
        }
        catch
        {
            if (subscription is not null) await subscription.DisposeAsync().ConfigureAwait(false);
            configWatch?.Dispose();
            if (registered && adapter is not null) botContext.UnregisterAdapter(adapter);
            if (adapter is not null)
                try
                {
                    await StopAdapterAsync(adapter).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    BotLog.Warning($"清理启动失败的 Adapter {instanceId} 时停止失败: {cleanupError.Message}");
                }

            loader.Unload();
            TryDeleteDirectory(Path.GetDirectoryName(loadAssemblyPath));
            throw;
        }

        return;

        void TraceLoad(string stage)
        {
            if (Environment.GetEnvironmentVariable("SHIROBOT_LIFECYCLE_DIAGNOSTICS") == "true")
                Console.WriteLine($"Adapter load probe: {stage}: {adapterPath}");
        }
    }

    private async Task StopEntryAsync(AdapterEntry entry, bool waitForAssemblyRelease = true)
    {
        var adapterId = entry.Id;
        var adapterName = entry.Metadata.Name;
        var assemblyPath = entry.AssemblyPath;
        var startedAt = Stopwatch.StartNew();
        // Return from the frame holding adapter objects before checking collectible ALC collection.
        // Assigning null in that frame does not release all Runtime Async/JIT temporaries.
        var (weakReference, references) = await BeginAdapterUnloadAsync(entry, startedAt).ConfigureAwait(false);
        if (!waitForAssemblyRelease)
        {
            RecordAdapterLifecycle(adapterId, "info",
                "adapter stopped for process exit; assembly collection will be left to process teardown");
            runtimeState.RecordEvent($"{adapterName} Adapter stopped");
            return;
        }

        RecordAdapterLifecycle(adapterId, "info",
            "collectible AssemblyLoadContext.Unload requested; waiting for collection");
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

        RecordAdapterLifecycle(adapterId, "info",
            $"Adapter ALC collected successfully after {startedAt.ElapsedMilliseconds} ms");
        runtimeState.RecordEvent($"{adapterName} Adapter unloaded");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<(WeakReference? Context, AdapterUnloadReferences References)> BeginAdapterUnloadAsync(
        AdapterEntry entry, Stopwatch startedAt)
    {
        var adapter = entry.Adapter ?? throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已进入卸载状态。");
        var subscription = entry.EventSubscription ??
                           throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已进入卸载状态。");
        var loader = entry.Loader ?? throw new InvalidOperationException($"Adapter {entry.Metadata.Name} 已进入卸载状态。");

        var adapterId = entry.Id;
        var assemblyPath = entry.AssemblyPath;
        var references = new AdapterUnloadReferences(
            new WeakReference(adapter),
            new WeakReference(subscription),
            entry.ConfigWatch is null ? null : new WeakReference(entry.ConfigWatch),
            entry.Configurable is null ? null : new WeakReference(entry.Configurable),
            entry.CurrentConfig is null ? null : new WeakReference(entry.CurrentConfig));
        RecordAdapterLifecycle(adapterId, "info",
            $"unload begin; assembly={assemblyPath}; adapter_ref={RuntimeHelpers.GetHashCode(adapter)}; subscription_ref={RuntimeHelpers.GetHashCode(subscription)}");

        try
        {
            using (BotLog.BeginScope(adapter.Logger))
            {
                await StopAdapterAsync(adapter).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Start unloading only after StopAsync succeeds so a failed stop remains retryable.
            RecordAdapterLifecycle(adapterId, "error",
                $"unload aborted in StopAsync after {startedAt.ElapsedMilliseconds} ms: {ex}");
            throw;
        }

        RecordAdapterLifecycle(adapterId, "info", $"StopAsync completed in {startedAt.ElapsedMilliseconds} ms");
        try
        {
            entry.ConfigWatch?.Dispose();
            RecordAdapterLifecycle(adapterId, "info", "config watcher disposed");
            await subscription.DisposeAsync().ConfigureAwait(false);
            RecordAdapterLifecycle(adapterId, "info",
                $"event subscription drained in {startedAt.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            RecordAdapterLifecycle(adapterId, "error",
                $"unload cleanup failed after {startedAt.ElapsedMilliseconds} ms: {ex}");
            throw;
        }

        botContext.UnregisterAdapter(adapter);
        RecordAdapterLifecycle(adapterId, "info", "adapter removed from BotContext");
        entry.ReleaseRuntimeReferences();
        RecordAdapterLifecycle(adapterId, "info", "AdapterEntry runtime references cleared");
        return (loader.BeginUnload(), references);
    }

    private void RecordAdapterLifecycle(string adapterId, string level, string message)
    {
        logHub.Record("system", level, $"Adapter lifecycle [{adapterId}]: {message}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForAdapterUnload(WeakReference? weakReference)
    {
        return DllLoader<IBotAdapter>.WaitForUnload(weakReference);
    }

    private async Task QueueConfigUpdateAsync(string adapterId, object candidate)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            AdapterEntry? entry;
            lock (_sync)
            {
                entry = _entries.Values.FirstOrDefault(item =>
                    string.Equals(item.Id, adapterId, StringComparison.OrdinalIgnoreCase));
            }

            if (entry is not null) await ApplyConfigCandidateAsync(entry, candidate).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logHub.Record("system", "error", $"Adapter 配置热重载失败: {adapterId} - {ex.Message}");
            runtimeState.RecordEvent($"Adapter config reload failed: {adapterId} - {ex.Message}", "error");
        }
        finally
        {
            _gate.Release();
        }
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
    ///     Applies a config candidate to a running adapter and returns the config now in effect.
    ///     RestartComponent adapters are stopped, reconfigured and started; on failure the previous
    ///     config is restored and the adapter restarted before the original error is rethrown.
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
            await StopAdapterAsync(adapter).ConfigureAwait(false);
            try
            {
                await configurable.ApplyConfigAsync(candidate).ConfigureAwait(false);
                await StartAdapterAsync(adapter).ConfigureAwait(false);
            }
            catch
            {
                // Optional rollback runs before rethrowing the original startup failure.
                // ReSharper disable once InvertIf
                if (current is not null)
                {
                    await configurable.ApplyConfigAsync(current).ConfigureAwait(false);
                    await StartAdapterAsync(adapter).ConfigureAwait(false);
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

    /// <summary>每次启动换一个新的生命周期作用域，配置触发的重启也拿到干净的作用域。</summary>
    internal static Task StartAdapterAsync(IBotAdapter adapter)
    {
        if (adapter is ILifetimeAwareAdapter aware) aware.Lifetime = new PluginLifetimeScope();
        return adapter.StartAsync();
    }

    /// <summary>
    ///     先发出停止信号，再调用适配器自己的 StopAsync（关闭连接会让接收循环退出），然后等待登记的任务结束并释放资源。
    ///     StopAsync 失败时不释放资源，保持可以重试。
    /// </summary>
    internal static async Task StopAdapterAsync(IBotAdapter adapter)
    {
        var scope = (adapter as ILifetimeAwareAdapter)?.Lifetime as PluginLifetimeScope;
        scope?.Cancel();
        await adapter.StopAsync().ConfigureAwait(false);
        if (scope is null) return;
        await scope.StopTasksAsync().ConfigureAwait(false);
        await scope.DisposeResourcesAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     先把配置复制成普通容器再序列化：直接用反射序列化适配器的配置类型会被 System.Text.Json 缓存，
    ///     拉住可回收的 ALC，适配器热重载过一次配置后就再也卸载不掉。
    /// </summary>
    private static string Fingerprint(object? config, Type configType)
    {
        try
        {
            return JsonSerializer.Serialize(ConfigFingerprintValue.Create(config));
        }
        catch
        {
            return RuntimeHelpers.GetHashCode(config ?? configType).ToString(CultureInfo.InvariantCulture);
        }
    }

    private void UpdateRuntimeState()
    {
        string[] names;
        lock (_sync)
        {
            names = [.. _entries.Values.Select(entry => entry.Metadata.Name)];
        }

        runtimeState.SetAdapter(names.Length == 0 ? "none" : string.Join(", ", names),
            names.Length == 0 ? "not_loaded" : "connected");
    }

    private void RecordError(string id, Exception exception)
    {
        lock (_sync)
        {
            _errors[id] = exception.Message;
            if (exception is ComponentUnloadPendingException) _restartRequiredErrors.Add(id);
            // Other errors must not clear the requirement to restart a resident old instance.
        }

        if (exception is ComponentUnloadPendingException)
            runtimeState.RecommendRestart("adapter:" + id, $"适配器 {id} 的旧程序集仍未释放，建议重启宿主完成清理。");
    }

    private static string CreateReloadShadow(string assemblyPath)
    {
        var sourceRoot = Path.GetDirectoryName(assemblyPath) ??
                         throw new InvalidOperationException("无法创建 Adapter 重载快照。");
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
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record AdapterUnloadReferences(
        WeakReference Adapter,
        WeakReference? Subscription,
        WeakReference? ConfigWatch,
        WeakReference? Configurable,
        WeakReference? CurrentConfig)
    {
        public List<string> GetAliveReferences()
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

    private sealed class AdapterEntry(
        string id,
        string configPath,
        string name,
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
        public string Id { get; } = id;
        public string ConfigPath { get; } = configPath;
        public string Name { get; } = name;
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
    string Id,
    string Name,
    string? Version,
    string? Platform,
    string? Description,
    string? AssemblyPath,
    bool Loaded,
    string? Error,
    bool RestartRequired);