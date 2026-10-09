using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShiroBot.Adapters.Compatibility;
using ShiroBot.Plugins.Compatibility;
using ShiroBot.Components.Updates;
using ShiroBot.Configuration;
using Tomlyn;

namespace ShiroBot.Adapters;

internal sealed class AdapterPackageManager(string adapterRoot, string? coreConfigPath = null)
{
    private const long MaxPackageBytes = 100L * 1024L * 1024L;
    private const long MaxExtractedBytes = 500L * 1024L * 1024L;
    private const int MaxArchiveEntries = 4096;
    private readonly string _root = Path.GetFullPath(adapterRoot);
    // Validate current instance configs once per manager; legacy registries are ignored.
    private bool _initialized;

    public IReadOnlyList<InstalledAdapterPackage> List()
    {
        if (!Directory.Exists(_root)) return [];
        return Directory.EnumerateDirectories(_root)
            .Select(ReadInstalled)
            .Where(package => package is not null)
            .Cast<InstalledAdapterPackage>()
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Package files are shared; descriptors/configs are host-owned and survive DLL updates.
    private static readonly object ProtocolGate = new();

    private string CoreConfigPath => coreConfigPath is null
        ? Path.Combine(Path.GetDirectoryName(_root)!, "config.toml")
        : Path.GetFullPath(coreConfigPath);
    private string PackageConfigPath(InstalledAdapterPackage package) => Path.Combine(GetAdapterDirectory(package.Id), "config.toml");

    public void InitializeInstances()
    {
        // Only current package configs are authoritative; no legacy state is imported.
        _initialized = true;
        _ = ListInstances();
    }

    public IReadOnlyList<InstalledAdapterInstance> ListInstances()
    {
        if (!_initialized) InitializeInstances();
        var result = new List<InstalledAdapterInstance>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in List())
        {
            var packageEnabled = IsPackageEnabled(package);
            var path = PackageConfigPath(package);
            if (!AdapterInstanceStore.IsDeclared(path))
            {
                // Older config without [[instances]]: the whole file is one instance named after the package.
                if (!ids.Add(package.Id)) throw new InvalidOperationException($"适配器实例 ID {package.Id} 重复，请使用全局唯一 ID。");
                result.Add(new(package.Id, package.Id, package.AssemblyPath, path, ReadImplicitEnabled(path), package.Name, packageEnabled, Implicit: true));
                continue;
            }
            foreach (var item in AdapterInstanceStore.Read(path))
            {
                ValidateInstanceId(item.Id);
                if (!ids.Add(item.Id)) throw new InvalidOperationException($"适配器实例 ID {item.Id} 重复，请使用全局唯一 ID。");
                result.Add(new(item.Id, package.Id, package.AssemblyPath, path, item.Enabled,
                    string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name, packageEnabled));
            }
        }
        return result.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public InstalledAdapterInstance? GetInstance(string id) => ListInstances().FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    private static void ValidateInstanceId(string id)
    {
        ValidateId(id);
        if (id.Length > 64 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new InvalidOperationException("实例 ID 最多 64 个字符，只能使用英文字母、数字、点、横线和下划线。");
    }

    public InstalledAdapterInstance CreateInstance(string packageId, string id, string? name)
    {
        ValidateInstanceId(id);
        if (name?.Length > 100) throw new InvalidOperationException("实例显示名称最多 100 个字符。");
        var package = Get(packageId) ?? throw new InvalidOperationException($"未安装 Adapter 包: {packageId}");
        if (GetInstance(id) is not null) throw new InvalidOperationException($"适配器实例 ID {id} 已存在。");
        var path = PackageConfigPath(package);
        Materialize(package);
        AdapterInstanceStore.Mutate(path, instances => instances.Add(new PackageAdapterInstance { Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim() }));
        return GetInstance(id)!;
    }

    /// <summary>
    /// Rewrites an implicit single-instance config into the [[instances]] layout, keeping its settings under the
    /// package-named instance. A loaded implicit instance must be restarted afterwards to read its new section.
    /// </summary>
    public bool Materialize(InstalledAdapterPackage package)
    {
        var path = PackageConfigPath(package);
        if (AdapterInstanceStore.IsDeclared(path)) return false;
        if (File.Exists(path) && !File.Exists(path + ".pre-instances.bak")) File.Copy(path, path + ".pre-instances.bak", overwrite: false);
        AdapterInstanceStore.Write(path, [new PackageAdapterInstance
            { Id = package.Id, Name = package.Name, Enabled = ReadImplicitEnabled(path), Config = AdapterInstanceStore.ReadObject(path) }], replaceLegacy: true);
        return true;
    }

    private sealed class ProtocolConfig
    {
        public string[] Protocols { get; set; } = [];
    }

    private string[] ReadProtocols() => File.Exists(CoreConfigPath)
        ? TomlSerializer.Deserialize<ProtocolConfig>(File.ReadAllText(CoreConfigPath),
            new TomlSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })?.Protocols ?? []
        : [];

    private bool IsProtocolEnabled(string id) => ReadProtocols().Any(value =>
        string.Equals(value.Trim(), id, StringComparison.OrdinalIgnoreCase));

    private static bool ReadImplicitEnabled(string path) =>
        AdapterInstanceStore.ReadObject(path).GetValueOrDefault("enabled") is true;

    public bool IsPackageEnabled(InstalledAdapterPackage package) => IsProtocolEnabled(package.Id);

    public void SetPackageEnabled(string id, bool enabled)
    {
        var package = Get(id) ?? throw new InvalidOperationException($"未安装 Adapter 包: {id}");
        lock (ProtocolGate)
        {
            var protocols = ReadProtocols().Where(value =>
                !string.Equals(value.Trim(), package.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (enabled) protocols.Add(package.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(CoreConfigPath)!);
            new ConfigManager(CoreConfigPath).SetConfigValue(CoreConfigPath, "protocols", protocols.ToArray());
        }
    }

    /// <summary>Renames an instance and/or changes its display name; its switch and connection config stay with it.</summary>
    public InstalledAdapterInstance UpdateInstance(string id, string newId, string? name)
    {
        var instance = GetInstance(id) ?? throw new InvalidOperationException($"未安装 Adapter 实例: {id}");
        newId = newId.Trim();
        ValidateInstanceId(newId);
        if (name?.Length > 100) throw new InvalidOperationException("实例显示名称最多 100 个字符。");
        if (!string.Equals(newId, instance.Id, StringComparison.OrdinalIgnoreCase) && GetInstance(newId) is not null)
            throw new InvalidOperationException($"适配器实例 ID {newId} 已存在。");
        if (instance.Implicit) Materialize(Get(instance.PackageId)!);
        AdapterInstanceStore.Update(instance.ConfigPath, instance.Id, item =>
        {
            item.Id = newId;
            if (name is not null) item.Name = string.IsNullOrWhiteSpace(name) ? newId : name.Trim();
        });
        return GetInstance(newId)!;
    }

    public void SetInstanceEnabled(string id, bool enabled)
    {
        var instance = GetInstance(id) ?? throw new InvalidOperationException($"未安装 Adapter 实例: {id}");
        if (instance.Implicit) new ConfigManager(instance.ConfigPath).SetConfigValue(instance.ConfigPath, "enabled", enabled);
        else AdapterInstanceStore.Update(instance.ConfigPath, id, item => item.Enabled = enabled);
    }

    public void DeleteInstance(string id)
    {
        var instance = GetInstance(id);
        if (instance is null) return;
        if (instance.Implicit) Materialize(Get(instance.PackageId)!);
        AdapterInstanceStore.Mutate(instance.ConfigPath, instances => instances.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)));
    }

    private void EnsureInstanceConfig(InstalledAdapterPackage package)
    {
        var path = PackageConfigPath(package);
        if (!AdapterInstanceStore.IsDeclared(path)) AdapterInstanceStore.Write(path, [], replaceLegacy: true);
    }

    public AdapterPackageProbe Prepare(string packagePath, string workRoot)
    {
        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath)) throw new FileNotFoundException("Adapter 包不存在。", fullPackagePath);
        if (new FileInfo(fullPackagePath).Length > MaxPackageBytes) throw new InvalidOperationException("Adapter 包不能超过 100MB。");

        var extension = Path.GetExtension(fullPackagePath);
        var extractRoot = Path.Combine(workRoot, "extract");
        string entryPath;
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            entryPath = fullPackagePath;
        }
        else if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(extractRoot);
            ExtractZipSafely(fullPackagePath, extractRoot);
            entryPath = FindSingleEntryAssembly(extractRoot);
        }
        else
        {
            throw new InvalidOperationException("只支持 .dll 或 .zip Adapter 包。");
        }

        var metadata = AdapterContractProbe.ReadMetadata(entryPath)
            ?? throw new InvalidOperationException("未找到有效的 BotAdapter 入口。");
        ValidateId(metadata.Id);
        ComponentApiCompatibility.EnsureCompatible("Adapter", metadata.Id, metadata.MinimumApiVersion, metadata.MaximumApiVersion);
        return new AdapterPackageProbe(
            metadata.Id,
            metadata.MinimumApiVersion,
            metadata.MaximumApiVersion,
            entryPath,
            extension.TrimStart('.').ToLowerInvariant(),
            metadata.SharedAssemblies,
            metadata.Name ?? metadata.Id,
            metadata.Version ?? "1.0.0",
            metadata.Description,
            metadata.Protocol);
    }

    public InstalledAdapterPackage Install(AdapterPackageProbe package, bool enabled)
    {
        if (Get(package.Id) is not null) InitializeInstances();
        Directory.CreateDirectory(_root);
        var target = GetAdapterDirectory(package.Id);
        var fresh = !Directory.Exists(target);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyPackageFiles(package, staging);
            var entryRelativePath = Path.GetRelativePath(
                package.Type == "zip" ? FindExtractRoot(package.EntryAssemblyPath) : Path.GetDirectoryName(package.EntryAssemblyPath)!,
                package.EntryAssemblyPath);
            if (package.Type == "dll") entryRelativePath = Path.GetFileName(package.EntryAssemblyPath);
            PreserveUserFiles(target, staging);

            if (Directory.Exists(target)) Directory.Move(target, backup);
            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }

            TryDeleteDirectory(backup);
            var installed = new InstalledAdapterPackage(package.Id, Path.Combine(target, entryRelativePath), enabled, package.Name, package.Version, package.Description, package.Platform);
            if (fresh) EnsureInstanceConfig(installed);
            SetPackageEnabled(package.Id, enabled);
            return installed;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>
    /// Installs a package and starts it when <paramref name="enabled"/> is set. If a replacement fails to
    /// start, the previous version is restored. A fresh install that fails to start is kept, disabled, so
    /// adapters that need configuration before their first start (credentials, endpoints) can be set up
    /// from the config page and started afterwards.
    /// </summary>
    public async Task<AdapterInstallResult> InstallAndActivateAsync(
        AdapterPackageProbe package,
        bool enabled,
        Func<InstalledAdapterPackage, Task> activate,
        Func<InstalledAdapterPackage, Task>? restore = null,
        bool activateWhenDisabled = false)
    {
        if (Get(package.Id) is not null) InitializeInstances();
        Directory.CreateDirectory(_root);
        var target = GetAdapterDirectory(package.Id);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        InstalledAdapterPackage? previous = null;
        var previousProtocolEnabled = IsProtocolEnabled(package.Id);
        try
        {
            previous = ReadInstalled(target);
            CopyPackageFiles(package, staging);
            var entryRelativePath = Path.GetRelativePath(
                package.Type == "zip" ? FindExtractRoot(package.EntryAssemblyPath) : Path.GetDirectoryName(package.EntryAssemblyPath)!,
                package.EntryAssemblyPath);
            if (package.Type == "dll") entryRelativePath = Path.GetFileName(package.EntryAssemblyPath);
            PreserveUserFiles(target, staging);

            if (Directory.Exists(target)) Directory.Move(target, backup);
            Directory.Move(staging, target);
            var installed = new InstalledAdapterPackage(package.Id, Path.Combine(target, entryRelativePath), enabled, package.Name, package.Version, package.Description, package.Platform);
            if (previous is null) EnsureInstanceConfig(installed);
            try
            {
                SetPackageEnabled(package.Id, enabled);
                if (enabled || activateWhenDisabled) await activate(installed).ConfigureAwait(false);
                TryDeleteDirectory(backup);
                return new AdapterInstallResult(installed, null);
            }
            catch (Exception activationError) when (previous is null)
            {
                SetEnabled(package.Id, false);
                return new AdapterInstallResult(installed with { Enabled = false }, activationError.Message);
            }
            catch (Exception activationError)
            {
                TryDeleteDirectory(target);
                if (Directory.Exists(backup)) Directory.Move(backup, target);
                SetPackageEnabled(package.Id, previousProtocolEnabled);
                if (previous is not null && restore is not null)
                {
                    var restored = ReadInstalled(target);
                    if (restored is not null)
                    {
                        try
                        {
                            await restore(restored).ConfigureAwait(false);
                        }
                        catch (Exception rollbackError)
                        {
                            throw new InvalidOperationException(
                                $"新 Adapter 启动失败，旧版本恢复也失败。启动错误: {activationError.Message}; 恢复错误: {rollbackError.Message}",
                                new AggregateException(activationError, rollbackError));
                        }
                    }
                }

                throw new InvalidOperationException($"Adapter 启动失败，已恢复旧版本: {activationError.Message}", activationError);
            }
        }
        finally
        {
            TryDeleteDirectory(staging);
            if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
        }
    }

    public void SetEnabled(string id, bool enabled) => SetPackageEnabled(id, enabled);

    /// <summary>
    /// Stages a package in <c>.update/&lt;id&gt;/</c> when the running version cannot be released; the next
    /// start applies it before any adapter is loaded. A newer staged package replaces an older one.
    /// </summary>
    public void StageUpdate(AdapterPackageProbe package, bool enabled)
    {
        ValidateId(package.Id);
        var staging = StagedComponentUpdates.GetStagingDirectory(_root, package.Id);
        TryDeleteDirectory(staging);
        CopyPackageFiles(package, staging);
        StagedComponentUpdates.WriteTarget(staging, GetAdapterDirectory(package.Id));
    }

    public bool HasStagedUpdate(string id) => StagedComponentUpdates.HasStaged(_root, id);

    /// <summary>Applies staged packages; call at startup before adapters are loaded.</summary>
    public (IReadOnlyList<string> Applied, IReadOnlyList<(string Id, string Error)> Failed) ApplyStagedUpdates() =>
        StagedComponentUpdates.ApplyStaged(_root);

    public InstalledAdapterPackage? Get(string id) => List().FirstOrDefault(package => string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Uninstall(string id)
    {
        var path = GetAdapterDirectory(id);
        if (!IsStrictChild(_root, path)) throw new InvalidOperationException("拒绝删除 Adapter 根目录之外的路径。");
        StagedComponentUpdates.DiscardStaged(_root, id);
        if (Get(id) is not null) SetPackageEnabled(id, false);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        TryDeleteCacheFile(GetMetadataCachePath(path));
    }

    private string GetMetadataCachePath(string directory)
    {
        // Include the full package path so separate adapter roots cannot share stale metadata.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory))));
        return Path.Combine(Path.GetDirectoryName(_root)!, "cache", "adapters", key + ".json");
    }

    private InstalledAdapterPackage? ReadInstalled(string directory)
    {
        try
        {
            var id = Path.GetFileName(directory);
            if (id.StartsWith('.')) return null;
            var files = Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new FileInfo(path))
                .Select(file => new AssemblyStamp(Path.GetRelativePath(directory, file.FullName), file.Length, file.LastWriteTimeUtc.Ticks))
                .ToArray();
            var cachePath = GetMetadataCachePath(directory);
            AdapterManifest? manifest = null;
            try
            {
                if (File.Exists(cachePath))
                {
                    var cached = JsonSerializer.Deserialize<AdapterMetadataCache>(File.ReadAllText(cachePath));
                    if (cached?.Files is not null && cached.Files.SequenceEqual(files)) manifest = cached.Manifest;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

            if (manifest is null || !string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(manifest.Entry) || !files.Any(file => file.Path == manifest.Entry))
            {
                var entries = files.Select(file => (file.Path, Metadata: AdapterContractProbe.ReadMetadata(Path.Combine(directory, file.Path))))
                    .Where(entry => entry.Metadata is not null).ToArray();
                if (entries.Length != 1 || !string.Equals(entries[0].Metadata!.Id, id, StringComparison.OrdinalIgnoreCase)) return null;
                var metadata = entries[0].Metadata!;
                manifest = new AdapterManifest(metadata.Id, entries[0].Path, metadata.Name, metadata.Version, metadata.Description, metadata.Protocol);
                TryWriteMetadataCache(cachePath, new AdapterMetadataCache(manifest, files));
            }

            // The former package-local manifest is redundant; never use it as configuration.
            TryDeleteCacheFile(Path.Combine(directory, "adapter.json"));
            return new InstalledAdapterPackage(manifest.Id, Path.Combine(directory, manifest.Entry), IsProtocolEnabled(manifest.Id),
                manifest.Name ?? manifest.Id, manifest.Version ?? "1.0.0", manifest.Description, manifest.Platform);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static void TryWriteMetadataCache(string path, AdapterMetadataCache cache)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(cache));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { TryDeleteCacheFile(temporary); }
    }

    private static void TryDeleteCacheFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record AssemblyStamp(string Path, long Length, long LastWriteTicks);
    private sealed record AdapterMetadataCache(AdapterManifest Manifest, AssemblyStamp[] Files);

    private static string FindSingleEntryAssembly(string root)
    {
        var entries = Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
            .Where(path => AdapterContractProbe.ReadMetadata(path) is not null)
            .ToArray();
        return entries.Length switch
        {
            1 => entries[0],
            0 => throw new InvalidOperationException("压缩包中未找到有效 Adapter DLL。"),
            _ => throw new InvalidOperationException("压缩包中包含多个 BotAdapter 入口。")
        };
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        var root = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxArchiveEntries) throw new InvalidOperationException("压缩包文件数量超过限制。");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxExtractedBytes - total) throw new InvalidOperationException("压缩包解压后大小超过限制。");
            total += entry.Length;
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!IsUnder(root, destination)) throw new InvalidOperationException("压缩包包含非法路径。");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static void CopyPackageFiles(AdapterPackageProbe package, string target)
    {
        var files = new List<string>();
        if (package.Type == "dll")
        {
            Directory.CreateDirectory(target);
            var fileName = Path.GetFileName(package.EntryAssemblyPath);
            File.Copy(package.EntryAssemblyPath, Path.Combine(target, fileName));
            files.Add(fileName);
        }
        else
        {
            var sourceRoot = FindExtractRoot(package.EntryAssemblyPath);
            foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceRoot, source);
                // Package ownership is recorded by the host, never taken from an uploaded manifest.
                if (relative == StagedComponentUpdates.PackageFilesName ||
                    relative.Equals("adapter.json", StringComparison.OrdinalIgnoreCase)) continue;
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
                files.Add(relative);
            }
        }
        File.WriteAllLines(Path.Combine(target, StagedComponentUpdates.PackageFilesName), files);
    }

    private static string FindExtractRoot(string entryPath)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(entryPath)!);
        while (current.Parent is not null && !string.Equals(current.Name, "extract", StringComparison.OrdinalIgnoreCase)) current = current.Parent;
        return current.FullName;
    }

    private string GetAdapterDirectory(string id)
    {
        ValidateId(id);
        return Path.Combine(_root, id);
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith('.') || id is "." or ".." || id != id.Trim() || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal) || id.Contains('/') || id.Contains('\\') ||
            string.Equals(id, "CON", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "PRN", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "AUX", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "NUL", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && id.Length == 4 && char.IsDigit(id[3]) || id.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) && id.Length == 4 && char.IsDigit(id[3]))
            throw new InvalidOperationException("Adapter ID 包含非法路径字符。");
    }

    private static bool IsUnder(string root, string path) =>
        path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsStrictChild(string root, string path) => IsUnder(root, path) && !string.Equals(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    private static void PreserveUserFiles(string target, string staging)
    {
        if (!Directory.Exists(target)) return;
        var manifest = Path.Combine(target, StagedComponentUpdates.PackageFilesName);
        // Older installs have no ownership list; retain unknown files rather than deleting user data.
        var oldPackageFiles = File.Exists(manifest)
            ? File.ReadAllLines(manifest).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(target, source);
            if (relative.Equals("adapter.json", StringComparison.OrdinalIgnoreCase) ||
                relative.Equals(StagedComponentUpdates.PackageFilesName, StringComparison.OrdinalIgnoreCase)) continue;
            var isConfig = Path.GetFileName(source).Equals("config.toml", StringComparison.OrdinalIgnoreCase);
            if (!isConfig && oldPackageFiles.Contains(relative)) continue;
            var destination = Path.Combine(staging, relative);
            if (!isConfig && File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: isConfig);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record AdapterManifest(string Id, string Entry, string? Name = null, string? Version = null, string? Description = null, string? Platform = null);
}

internal sealed record AdapterPackageProbe(string Id, string MinimumApiVersion, string MaximumApiVersion, string EntryAssemblyPath, string Type, IReadOnlyList<string> SharedAssemblies, string Name, string Version, string? Description, string? Platform);
/// <summary>Result of an install; <see cref="StartError"/> is set when a fresh install was kept but did not start.</summary>
internal sealed record AdapterInstallResult(InstalledAdapterPackage Package, string? StartError);

internal sealed record InstalledAdapterPackage(string Id, string AssemblyPath, bool Enabled, string Name, string Version, string? Description, string? Platform);


/// <param name="Implicit">An older config without [[instances]]: the file root is this instance's config.</param>
internal sealed record InstalledAdapterInstance(string Id, string PackageId, string AssemblyPath, string ConfigPath, bool Enabled, string Name, bool PackageEnabled = true, bool Implicit = false)
{
    /// <summary>Loads at startup: both the instance and its package switch are on.</summary>
    public bool Active => Enabled && PackageEnabled;
}
