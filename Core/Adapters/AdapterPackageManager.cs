using System.IO.Compression;
using System.Text.Json;
using ShiroBot.Adapters.Compatibility;
using ShiroBot.Plugins.Compatibility;

namespace ShiroBot.Adapters;

internal sealed class AdapterPackageManager(string adapterRoot)
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
            PreserveUserConfig(target, staging);
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
            return new InstalledAdapterPackage(package.Id, Path.Combine(target, entryRelativePath), enabled, package.Name, package.Version, package.Description, package.Platform);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public async Task<InstalledAdapterPackage> InstallAndActivateAsync(
        AdapterPackageProbe package,
        bool enabled,
        Func<InstalledAdapterPackage, Task> activate,
        Func<InstalledAdapterPackage, Task>? restore = null)
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
            PreserveUserConfig(target, staging);
            File.WriteAllText(
                Path.Combine(staging, "adapter.json"),
                JsonSerializer.Serialize(new AdapterManifest(package.Id, entryRelativePath, enabled, package.Name, package.Version, package.Description, package.Platform)));

            if (Directory.Exists(target)) Directory.Move(target, backup);
            Directory.Move(staging, target);
            var installed = new InstalledAdapterPackage(package.Id, Path.Combine(target, entryRelativePath), enabled, package.Name, package.Version, package.Description, package.Platform);
            try
            {
                if (enabled) await activate(installed).ConfigureAwait(false);
                TryDeleteDirectory(backup);
                return installed;
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

                throw new InvalidOperationException(
                    previous is null
                        ? $"Adapter 启动失败，安装已回滚: {activationError.Message}"
                        : $"Adapter 启动失败，已恢复旧版本: {activationError.Message}",
                    activationError);
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

    public InstalledAdapterPackage? Get(string id) => List().FirstOrDefault(package => string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Uninstall(string id)
    {
        var path = GetAdapterDirectory(id);
        if (!Directory.Exists(path)) throw new InvalidOperationException($"未安装 Adapter: {id}");
        if (!IsStrictChild(_root, path)) throw new InvalidOperationException("拒绝删除 Adapter 根目录之外的路径。");
        Directory.Delete(path, recursive: true);
    }

    private InstalledAdapterPackage? ReadInstalled(string directory)
    {
        try
        {
            var manifestPath = Path.Combine(directory, "adapter.json");
            if (!File.Exists(manifestPath)) return null;
            var manifest = JsonSerializer.Deserialize<AdapterManifest>(File.ReadAllText(manifestPath));
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Entry)) return null;
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
        if (package.Type == "dll")
        {
            Directory.CreateDirectory(target);
            File.Copy(package.EntryAssemblyPath, Path.Combine(target, Path.GetFileName(package.EntryAssemblyPath)));
            return;
        }
        var sourceRoot = package.Type == "zip" ? FindExtractRoot(package.EntryAssemblyPath) : Path.GetDirectoryName(package.EntryAssemblyPath)!;
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
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
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id != id.Trim() || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal) || id.Contains('/') || id.Contains('\\') ||
            string.Equals(id, "CON", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "PRN", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "AUX", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "NUL", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && id.Length == 4 && char.IsDigit(id[3]) || id.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) && id.Length == 4 && char.IsDigit(id[3]))
            throw new InvalidOperationException("Adapter ID 包含非法路径字符。");
    }

    private static bool IsUnder(string root, string path) =>
        path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsStrictChild(string root, string path) => IsUnder(root, path) && !string.Equals(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    private static void PreserveUserConfig(string target, string staging)
    {
        var config = Path.Combine(target, "config.toml");
        if (File.Exists(config)) File.Copy(config, Path.Combine(staging, "config.toml"), overwrite: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record AdapterManifest(string Id, string Entry, bool Enabled, string? Name = null, string? Version = null, string? Description = null, string? Platform = null);
}

internal sealed record AdapterPackageProbe(string Id, string MinimumApiVersion, string MaximumApiVersion, string EntryAssemblyPath, string Type, IReadOnlyList<string> SharedAssemblies, string Name, string Version, string? Description, string? Platform);
internal sealed record InstalledAdapterPackage(string Id, string AssemblyPath, bool Enabled, string Name, string Version, string? Description, string? Platform);
