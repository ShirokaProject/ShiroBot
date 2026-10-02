using System.IO.Compression;
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
    private string InstanceRoot => Path.Combine(_root, ".instances");

    private string CoreConfigPath => coreConfigPath is null
        ? Path.Combine(Path.GetDirectoryName(_root)!, "config.toml")
        : Path.GetFullPath(coreConfigPath);
    public bool HasDeclaredInstances => ReadDeclaredInstances() is not null;

    public void InitializeInstances(IEnumerable<string>? requestedAdapters = null)
    {
        if (HasDeclaredInstances) return;
        var legacy = CurrentDeclarations();
        if (legacy.Length == 0) return;
        var requested = (requestedAdapters ?? []).ToArray();
        foreach (var instance in legacy)
        {
            if (!string.Equals(instance.Id, instance.PackageId, StringComparison.OrdinalIgnoreCase)) continue;
            var package = Get(instance.PackageId)!;
            if (requested.Any(value => string.Equals(value, package.Id, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(value, package.Name, StringComparison.OrdinalIgnoreCase) ||
                                       File.Exists(value) && string.Equals(Path.GetFullPath(value), package.AssemblyPath, StringComparison.OrdinalIgnoreCase)))
                instance.Enabled = true;
        }
        SaveDeclaredInstances(legacy);
    }

    private AdapterInstanceConfig[]? ReadDeclaredInstances()
    {
        if (!File.Exists(CoreConfigPath)) return null;
        var config = TomlSerializer.Deserialize<CoreConfig>(File.ReadAllText(CoreConfigPath), new TomlSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var instances = config?.AdapterInstances;
        if (instances is null) return null;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in instances)
        {
            ValidateId(instance.Id);
            ValidateId(instance.PackageId);
            if (!ids.Add(instance.Id)) throw new InvalidOperationException($"实例 ID {instance.Id} 重复出现在 adapter_instances 中。");
            if (Get(instance.Id) is { } reserved && !string.Equals(reserved.Id, instance.PackageId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"实例 ID {instance.Id} 与另一个适配器包冲突。");
        }
        return instances;
    }

    private void SaveDeclaredInstances(IEnumerable<AdapterInstanceConfig> instances)
    {
        var values = instances.Select(instance => (object)new Dictionary<string, object?>
        {
            ["id"] = instance.Id, ["package_id"] = instance.PackageId, ["name"] = instance.Name, ["enabled"] = instance.Enabled
        }).ToList();
        new ConfigManager(CoreConfigPath).ReplaceConfigValue(CoreConfigPath, "adapter_instances", values);
    }

    private AdapterInstanceConfig[] CurrentDeclarations() => ReadDeclaredInstances() ?? ListLegacyInstances().Select(instance =>
        new AdapterInstanceConfig { Id = instance.Id, PackageId = instance.PackageId, Name = instance.Name, Enabled = instance.Enabled }).ToArray();

    public IReadOnlyList<InstalledAdapterInstance> ListInstances()
    {
        var declared = ReadDeclaredInstances();
        if (declared is null) return ListLegacyInstances();
        return declared.Select(instance =>
        {
            var package = Get(instance.PackageId) ?? throw new InvalidOperationException($"实例 {instance.Id} 引用未安装的适配器包 {instance.PackageId}。");
            var configPath = string.Equals(instance.Id, package.Id, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Path.GetDirectoryName(package.AssemblyPath)!, "config.toml")
                : Path.Combine(InstanceRoot, instance.Id, "config.toml");
            return new InstalledAdapterInstance(instance.Id, package.Id, package.AssemblyPath, configPath,
                instance.Enabled, string.IsNullOrWhiteSpace(instance.Name) ? instance.Id : instance.Name);
        }).OrderBy(instance => instance.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<InstalledAdapterInstance> ListLegacyInstances()

    {
        var descriptors = Directory.Exists(InstanceRoot)
            ? Directory.EnumerateFiles(InstanceRoot, "instance.json", SearchOption.AllDirectories)
                .Select(path => ReadInstanceDescriptor(path))
                .Where(item => item is not null).Cast<AdapterInstanceManifest>().ToArray()
            : [];
        var result = new List<InstalledAdapterInstance>();
        foreach (var package in List())
        {
            var defaultDescriptor = descriptors.FirstOrDefault(item => string.Equals(item.Id, package.Id, StringComparison.OrdinalIgnoreCase));
            if (defaultDescriptor?.Deleted != true)
                result.Add(new(package.Id, package.Id, package.AssemblyPath,
                    Path.Combine(Path.GetDirectoryName(package.AssemblyPath)!, "config.toml"),
                    package.Enabled, package.Name));
            foreach (var descriptor in descriptors.Where(item => !item.Deleted &&
                         string.Equals(item.PackageId, package.Id, StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(item.Id, package.Id, StringComparison.OrdinalIgnoreCase)))
                result.Add(new(descriptor.Id, package.Id, package.AssemblyPath,
                    Path.Combine(InstanceRoot, descriptor.Id, "config.toml"), descriptor.Enabled, descriptor.Name));
        }
        return result.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public InstalledAdapterInstance? GetInstance(string id) => ListInstances().FirstOrDefault(item =>
        string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    public InstalledAdapterInstance CreateInstance(string packageId, string id, string? name)
    {
        ValidateId(id);
        if (id.Length > 64 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new InvalidOperationException("实例 ID 最多 64 个字符，只能使用英文字母、数字、点、横线和下划线。");
        if (name?.Length > 100) throw new InvalidOperationException("实例显示名称最多 100 个字符。");
        var package = Get(packageId) ?? throw new InvalidOperationException($"未安装 Adapter 包: {packageId}");
        if (Get(id) is not null || GetInstance(id) is not null || Directory.Exists(Path.Combine(InstanceRoot, id)))
            throw new InvalidOperationException($"适配器实例 ID {id} 已存在。");
        var directory = Path.Combine(InstanceRoot, id);
        Directory.CreateDirectory(directory);
        var declarations = CurrentDeclarations().ToList();
        declarations.Add(new AdapterInstanceConfig { Id = id, PackageId = package.Id, Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim() });
        try
        {
            // Start empty rather than copying another bot's credentials.
            File.WriteAllText(Path.Combine(directory, "config.toml"), "");
            SaveDeclaredInstances(declarations);
        }
        catch { TryDeleteDirectory(directory); throw; }
        return GetInstance(id)!;
    }

    public void SetInstanceEnabled(string id, bool enabled)
    {
        var instance = GetInstance(id) ?? throw new InvalidOperationException($"未安装 Adapter 实例: {id}");
        var declarations = CurrentDeclarations();
        declarations.Single(item => string.Equals(item.Id, instance.Id, StringComparison.OrdinalIgnoreCase)).Enabled = enabled;
        SaveDeclaredInstances(declarations);
        if (string.Equals(instance.Id, instance.PackageId, StringComparison.OrdinalIgnoreCase)) SetEnabled(instance.PackageId, enabled);
    }

    public void DeleteInstance(string id)
    {
        ValidateId(id);
        var instance = GetInstance(id);
        if (instance is null)
        {
            if (Get(id) is null) Uninstall(id);
            return;
        }
        var declarations = CurrentDeclarations().Where(item => !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        var siblings = declarations.Where(item => string.Equals(item.PackageId, instance.PackageId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (siblings.Length == 0) { Uninstall(instance.PackageId); return; }
        SaveDeclaredInstances(declarations);
        if (string.Equals(instance.Id, instance.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            SetEnabled(instance.PackageId, false);
            File.Delete(instance.ConfigPath);
        }
        else TryDeleteDirectory(Path.Combine(InstanceRoot, instance.Id));
    }

    private void EnsureInstalledDefault(InstalledAdapterPackage package)
    {
        var declarations = CurrentDeclarations().ToList();
        if (!declarations.Any(instance => string.Equals(instance.PackageId, package.Id, StringComparison.OrdinalIgnoreCase)))
            declarations.Add(new AdapterInstanceConfig { Id = package.Id, PackageId = package.Id, Name = package.Name, Enabled = package.Enabled });
        SaveDeclaredInstances(declarations);
    }

    private static AdapterInstanceManifest? ReadInstanceDescriptor(string path)
    {
        try
        {
            var descriptor = JsonSerializer.Deserialize<AdapterInstanceManifest>(File.ReadAllText(path));
            if (descriptor is null) return null;
            ValidateId(descriptor.Id);
            ValidateId(descriptor.PackageId);
            return string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), descriptor.Id, StringComparison.OrdinalIgnoreCase)
                ? descriptor : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return null; }
    }

    private sealed record AdapterInstanceManifest(string Id, string PackageId, string Name, bool Enabled, bool Deleted = false);

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
        if (GetInstance(metadata.Id) is { } collision && !string.Equals(collision.PackageId, metadata.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"包 ID {metadata.Id} 已被其他适配器的实例占用。");
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
        Directory.CreateDirectory(_root);
        var target = GetAdapterDirectory(package.Id);
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
            var manifest = new AdapterManifest(package.Id, entryRelativePath, enabled, package.Name, package.Version, package.Description, package.Platform);
            File.WriteAllText(Path.Combine(staging, "adapter.json"), JsonSerializer.Serialize(manifest));

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
            EnsureInstalledDefault(installed);
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
        Directory.CreateDirectory(_root);
        var target = GetAdapterDirectory(package.Id);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        InstalledAdapterPackage? previous = null;
        try
        {
            previous = ReadInstalled(target);
            CopyPackageFiles(package, staging);
            var entryRelativePath = Path.GetRelativePath(
                package.Type == "zip" ? FindExtractRoot(package.EntryAssemblyPath) : Path.GetDirectoryName(package.EntryAssemblyPath)!,
                package.EntryAssemblyPath);
            if (package.Type == "dll") entryRelativePath = Path.GetFileName(package.EntryAssemblyPath);
            PreserveUserFiles(target, staging);
            File.WriteAllText(
                Path.Combine(staging, "adapter.json"),
                JsonSerializer.Serialize(new AdapterManifest(package.Id, entryRelativePath, enabled, package.Name, package.Version, package.Description, package.Platform)));

            if (Directory.Exists(target)) Directory.Move(target, backup);
            Directory.Move(staging, target);
            var installed = new InstalledAdapterPackage(package.Id, Path.Combine(target, entryRelativePath), enabled, package.Name, package.Version, package.Description, package.Platform);
            if (previous is null) EnsureInstalledDefault(installed);
            try
            {
                if (enabled || activateWhenDisabled) await activate(installed).ConfigureAwait(false);
                TryDeleteDirectory(backup);
                return new AdapterInstallResult(installed, null);
            }
            catch (Exception activationError) when (previous is null)
            {
                SetInstanceEnabled(package.Id, false);
                return new AdapterInstallResult(installed with { Enabled = false }, activationError.Message);
            }
            catch (Exception activationError)
            {
                TryDeleteDirectory(target);
                if (Directory.Exists(backup)) Directory.Move(backup, target);
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

    public void SetEnabled(string id, bool enabled)
    {
        var installed = Get(id) ?? throw new InvalidOperationException($"未安装 Adapter: {id}");
        File.WriteAllText(Path.Combine(GetAdapterDirectory(installed.Id), "adapter.json"),
            JsonSerializer.Serialize(new AdapterManifest(installed.Id, Path.GetRelativePath(GetAdapterDirectory(installed.Id), installed.AssemblyPath), enabled, installed.Name, installed.Version, installed.Description, installed.Platform)));
    }

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
        var entryRelativePath = package.Type == "dll"
            ? Path.GetFileName(package.EntryAssemblyPath)
            : Path.GetRelativePath(FindExtractRoot(package.EntryAssemblyPath), package.EntryAssemblyPath);
        File.WriteAllText(
            Path.Combine(staging, "adapter.json"),
            JsonSerializer.Serialize(new AdapterManifest(package.Id, entryRelativePath, enabled, package.Name, package.Version, package.Description, package.Platform)));
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
        var declarations = CurrentDeclarations();
        foreach (var instance in declarations.Where(item => string.Equals(item.PackageId, id, StringComparison.OrdinalIgnoreCase)))
        {
            var directory = Path.Combine(InstanceRoot, instance.Id);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        var tombstone = Path.Combine(InstanceRoot, id);
        if (Directory.Exists(tombstone)) Directory.Delete(tombstone, true);
        StagedComponentUpdates.DiscardStaged(_root, id);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        SaveDeclaredInstances(declarations.Where(item => !string.Equals(item.PackageId, id, StringComparison.OrdinalIgnoreCase)));
    }

    private InstalledAdapterPackage? ReadInstalled(string directory)
    {
        try
        {
            var manifestPath = Path.Combine(directory, "adapter.json");
            if (!File.Exists(manifestPath)) return null;
            var manifest = JsonSerializer.Deserialize<AdapterManifest>(File.ReadAllText(manifestPath));
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Entry) ||
                !string.Equals(Path.GetFileName(directory), manifest.Id, StringComparison.OrdinalIgnoreCase)) return null;
            var assemblyPath = Path.GetFullPath(Path.Combine(directory, manifest.Entry));
            if (!IsUnder(directory, assemblyPath) || !File.Exists(assemblyPath)) return null;
            return new InstalledAdapterPackage(manifest.Id, assemblyPath, manifest.Enabled, manifest.Name ?? manifest.Id, manifest.Version ?? "1.0.0", manifest.Description, manifest.Platform);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

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
                if (relative == StagedComponentUpdates.PackageFilesName) continue;
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

    private sealed record AdapterManifest(string Id, string Entry, bool Enabled, string? Name = null, string? Version = null, string? Description = null, string? Platform = null);
}

internal sealed record AdapterPackageProbe(string Id, string MinimumApiVersion, string MaximumApiVersion, string EntryAssemblyPath, string Type, IReadOnlyList<string> SharedAssemblies, string Name, string Version, string? Description, string? Platform);
/// <summary>Result of an install; <see cref="StartError"/> is set when a fresh install was kept but did not start.</summary>
internal sealed record AdapterInstallResult(InstalledAdapterPackage Package, string? StartError);

internal sealed record InstalledAdapterPackage(string Id, string AssemblyPath, bool Enabled, string Name, string Version, string? Description, string? Platform);

internal sealed record InstalledAdapterInstance(string Id, string PackageId, string AssemblyPath, string ConfigPath, bool Enabled, string Name);
