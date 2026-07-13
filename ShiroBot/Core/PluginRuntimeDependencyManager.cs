using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShiroBot.Core;

internal sealed class PluginDependencyLayout
{
    public static PluginDependencyLayout Empty { get; } = new([]);

    private readonly IReadOnlyDictionary<string, string> _nativeAssets;

    public PluginDependencyLayout(IEnumerable<string> nativeAssets)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nativeAsset in nativeAssets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(nativeAsset);
            foreach (var alias in GetNativeAliases(Path.GetFileName(fullPath)))
            {
                aliases.TryAdd(alias, fullPath);
            }
        }

        _nativeAssets = aliases;
    }

    public string? ResolveNativeAsset(string unmanagedDllName)
    {
        if (_nativeAssets.TryGetValue(unmanagedDllName, out var exactPath))
        {
            return exactPath;
        }

        foreach (var candidate in GetNativeAliases(unmanagedDllName))
        {
            if (_nativeAssets.TryGetValue(candidate, out var path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetNativeAliases(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) yield break;

        yield return fileName;

        var name = fileName;
        foreach (var extension in new[] { ".dll", ".dylib", ".so" })
        {
            var extensionIndex = name.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (extensionIndex <= 0) continue;

            name = name[..extensionIndex];
            yield return name;
            break;
        }

        if (name.StartsWith("lib", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
        {
            yield return name[3..];
        }
    }
}

internal static class PluginRuntimeDependencyManager
{
    private const string ManifestResourceName = "ShiroBot.PluginRuntimeManifest.json";
    private const string CompleteMarkerName = ".complete";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<PluginDependencyLayout> PrepareAsync(
        string pluginAssemblyPath,
        string pluginDirectory,
        CancellationToken cancellationToken = default)
    {
        var manifestBytes = TryReadEmbeddedManifest(pluginAssemblyPath);
        if (manifestBytes is null)
        {
            return PluginDependencyLayout.Empty;
        }

        var manifest = JsonSerializer.Deserialize<PluginRuntimeManifest>(manifestBytes, JsonOptions)
                       ?? throw new InvalidOperationException("插件 native 依赖清单为空。");
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"不支持的插件 native 依赖清单版本: {manifest.SchemaVersion}");
        }

        if (!Uri.TryCreate(manifest.Source, UriKind.Absolute, out var packageSource) ||
            packageSource.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"插件 native NuGet 源必须是 HTTPS 地址: {manifest.Source}");
        }

        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        var extractedAssets = new List<string>();
        var selectedPackageCount = 0;

        foreach (var package in manifest.Packages)
        {
            var selectedAssets = SelectAssets(package.Assets, runtimeIdentifier);
            if (selectedAssets.Count == 0)
            {
                continue;
            }

            selectedPackageCount++;
            extractedAssets.AddRange(await EnsurePackageAssetsAsync(
                packageSource,
                package,
                selectedAssets,
                pluginDirectory,
                runtimeIdentifier,
                cancellationToken).ConfigureAwait(false));
        }

        if (manifest.Packages.Count > 0 && selectedPackageCount == 0)
        {
            var supportedRids = manifest.Packages
                .SelectMany(package => package.Assets)
                .Select(asset => asset.Rid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(rid => rid, StringComparer.OrdinalIgnoreCase);
            throw new PlatformNotSupportedException(
                $"插件没有适用于 {runtimeIdentifier} 的 native 依赖。支持: {string.Join(", ", supportedRids)}");
        }

        return new PluginDependencyLayout(extractedAssets);
    }

    private static IReadOnlyList<PluginRuntimeAsset> SelectAssets(
        IReadOnlyList<PluginRuntimeAsset> assets,
        string runtimeIdentifier)
    {
        foreach (var rid in EnumerateRidCandidates(runtimeIdentifier))
        {
            var matches = assets
                .Where(asset => string.Equals(asset.Rid, rid, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length > 0)
            {
                return matches;
            }
        }

        return [];
    }

    private static IEnumerable<string> EnumerateRidCandidates(string runtimeIdentifier)
    {
        yield return runtimeIdentifier;

        if (runtimeIdentifier.StartsWith("linux-musl-", StringComparison.OrdinalIgnoreCase))
        {
            yield return "linux-musl";
        }
        else if (runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase))
        {
            yield return "linux";
        }
        else if (runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase))
        {
            yield return "osx";
        }
        else if (runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
        {
            yield return "win";
        }

        yield return "any";
    }

    private static async Task<IReadOnlyList<string>> EnsurePackageAssetsAsync(
        Uri packageSource,
        PluginRuntimePackage package,
        IReadOnlyList<PluginRuntimeAsset> selectedAssets,
        string pluginDirectory,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        ValidatePackageIdentity(package);

        var packageIdLower = package.Id.ToLowerInvariant();
        var versionLower = package.Version.ToLowerInvariant();
        var cacheDirectory = Path.Combine(
            Path.GetFullPath(pluginDirectory),
            ".shirobot",
            "native",
            packageIdLower,
            versionLower,
            runtimeIdentifier);
        var markerPath = Path.Combine(cacheDirectory, CompleteMarkerName);
        var expectedFiles = selectedAssets
            .Select(asset => Path.Combine(cacheDirectory, Path.GetFileName(asset.Path)))
            .ToArray();

        if (File.Exists(markerPath) &&
            string.Equals((await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false)).Trim(), package.Sha512, StringComparison.Ordinal) &&
            expectedFiles.All(File.Exists))
        {
            return expectedFiles;
        }

        var cacheParent = Path.GetDirectoryName(cacheDirectory)
                          ?? throw new InvalidOperationException("无法确定插件 native 缓存目录。");
        Directory.CreateDirectory(cacheParent);

        var stagingDirectory = cacheDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        var packagePath = Path.Combine(cacheParent, $"{packageIdLower}.{versionLower}.{Guid.NewGuid():N}.nupkg");
        try
        {
            var packageUri = new Uri(
                packageSource.ToString().TrimEnd('/') +
                $"/{Uri.EscapeDataString(packageIdLower)}/{Uri.EscapeDataString(versionLower)}/{Uri.EscapeDataString(packageIdLower)}.{Uri.EscapeDataString(versionLower)}.nupkg");

            ConsoleHelper.Info($"正在下载插件 native 依赖: {package.Id} {package.Version} ({runtimeIdentifier})");
            await DownloadPackageAsync(packageUri, packagePath, cancellationToken).ConfigureAwait(false);
            await VerifyPackageHashAsync(packagePath, package.Sha512, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(stagingDirectory);
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                foreach (var asset in selectedAssets)
                {
                    var entry = archive.Entries.FirstOrDefault(candidate =>
                        string.Equals(NormalizePackagePath(candidate.FullName), NormalizePackagePath(asset.Path), StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"NuGet 包 {package.Id} {package.Version} 中缺少 native 文件: {asset.Path}");
                    var destinationPath = Path.Combine(stagingDirectory, Path.GetFileName(asset.Path));
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }

            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, CompleteMarkerName),
                package.Sha512,
                cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
            Directory.Move(stagingDirectory, cacheDirectory);
            ConsoleHelper.Success($"插件 native 依赖已准备: {package.Id} {package.Version} ({runtimeIdentifier})");
            return selectedAssets
                .Select(asset => Path.Combine(cacheDirectory, Path.GetFileName(asset.Path)))
                .ToArray();
        }
        finally
        {
            TryDeleteFile(packagePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static async Task DownloadPackageAsync(
        Uri packageUri,
        string packagePath,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            packageUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(packagePath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyPackageHashAsync(
        string packagePath,
        string expectedSha512,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha512))
        {
            throw new InvalidOperationException("插件 native NuGet 依赖缺少 SHA512，已拒绝下载。");
        }

        await using var stream = File.OpenRead(packagePath);
        using var sha512 = SHA512.Create();
        var actualHash = Convert.ToBase64String(await sha512.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(actualHash),
                Convert.FromBase64String(expectedSha512)))
        {
            throw new InvalidDataException("插件 native NuGet 包 SHA512 校验失败。");
        }
    }

    private static void ValidatePackageIdentity(PluginRuntimePackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Id) || package.Id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')))
        {
            throw new InvalidOperationException($"非法 NuGet 包 ID: {package.Id}");
        }

        if (string.IsNullOrWhiteSpace(package.Version) || package.Version.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '+')))
        {
            throw new InvalidOperationException($"非法 NuGet 包版本: {package.Version}");
        }
    }

    private static byte[]? TryReadEmbeddedManifest(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata || peReader.PEHeaders.CorHeader is null)
        {
            return null;
        }

        var metadataReader = peReader.GetMetadataReader();
        foreach (var resourceHandle in metadataReader.ManifestResources)
        {
            var resource = metadataReader.GetManifestResource(resourceHandle);
            if (!resource.Implementation.IsNil ||
                !string.Equals(metadataReader.GetString(resource.Name), ManifestResourceName, StringComparison.Ordinal))
            {
                continue;
            }

            var resourcesDirectory = peReader.PEHeaders.CorHeader.ResourcesDirectory;
            var section = peReader.GetSectionData(resourcesDirectory.RelativeVirtualAddress);
            var offset = checked((int)resource.Offset);
            var reader = section.GetReader(offset, section.Length - offset);
            var length = reader.ReadInt32();
            if (length < 0 || length > reader.RemainingBytes)
            {
                throw new BadImageFormatException("插件 native 依赖清单资源长度无效。");
            }

            return reader.ReadBytes(length);
        }

        return null;
    }

    private static string NormalizePackagePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temporary package cleanup is best effort.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Staging cleanup is best effort.
        }
    }

    private sealed record PluginRuntimeManifest(
        int SchemaVersion,
        string Source,
        IReadOnlyList<PluginRuntimePackage> Packages);

    private sealed record PluginRuntimePackage(
        string Id,
        string Version,
        string Sha512,
        IReadOnlyList<PluginRuntimeAsset> Assets);

    private sealed record PluginRuntimeAsset(string Rid, string Path);
}
