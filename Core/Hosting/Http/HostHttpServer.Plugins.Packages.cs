using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Context;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Marketplace;
using ShiroBot.Update;
using ShiroBot.Console;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static LoadedPluginHandle? FindLoadedPlugin(PluginManager pluginManager, string id) =>
        pluginManager.GetLoadedPluginSnapshot().FirstOrDefault(plugin =>
            string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plugin.DisplayName, id, StringComparison.OrdinalIgnoreCase));

    private static void DisablePluginFile(string assemblyPath)
    {
        var disabledPath = GetDisabledPluginPath(assemblyPath);
        if (File.Exists(disabledPath)) File.Delete(disabledPath);
        File.Move(assemblyPath, disabledPath);
    }

    private static void RestoreDisabledPluginFile(PluginManager pluginManager, string id)
    {
        var disabledPath = FindDisabledPluginFile(pluginManager, id);
        if (disabledPath is null) return;

        var enabledPath = disabledPath[..^DisabledPluginSuffix.Length];
        if (File.Exists(enabledPath)) File.Delete(enabledPath);
        File.Move(disabledPath, enabledPath);
    }

    private static string? FindDisabledPluginFile(PluginManager pluginManager, string id)
    {
        var pluginRootPath = pluginManager.PluginRootPath;
        var pluginRoot = Path.GetFullPath(pluginRootPath);
        if (!Directory.Exists(pluginRoot)) return null;

        var aliases = GetPluginIdAliases(id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(pluginRoot, "*.dll.disable", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path =>
            {
                var info = TryProbeDisabledPluginInfo(pluginManager, path);
                var enabledFileName = Path.GetFileNameWithoutExtension(path);
                var fileName = Path.GetFileNameWithoutExtension(enabledFileName);
                var directoryName = new DirectoryInfo(Path.GetDirectoryName(path) ?? pluginRoot).Name;

                return aliases.Contains(fileName) ||
                       aliases.Contains(directoryName) ||
                       (fileName.StartsWith("ShiroBot.", StringComparison.OrdinalIgnoreCase) && aliases.Contains(fileName["ShiroBot.".Length..])) ||
                       NameMatches(info?.Id, aliases) ||
                       NameMatches(info?.Name, aliases);
            });
    }

    private static InstalledPluginInfo? FindInstalledPlugin(PluginManager pluginManager, string id)
    {
        var loaded = FindLoadedPlugin(pluginManager, id);
        if (loaded is not null)
        {
            return new InstalledPluginInfo(loaded.AssemblyPath, loaded.Version);
        }

        var candidate = pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var info = pluginManager.TryProbePluginInfoFile(candidate);
            return new InstalledPluginInfo(candidate, info?.Version ?? string.Empty);
        }

        var disabled = FindDisabledPluginFile(pluginManager, id);
        if (!string.IsNullOrWhiteSpace(disabled))
        {
            var info = TryProbeDisabledPluginInfo(pluginManager, disabled);
            return new InstalledPluginInfo(disabled, info?.Version ?? string.Empty);
        }

        return null;
    }

    private static IEnumerable<PluginListItem> EnumerateDisabledPlugins(PluginManager pluginManager)
    {
        var pluginRootPath = pluginManager.PluginRootPath;
        var pluginRoot = Path.GetFullPath(pluginRootPath);
        if (!Directory.Exists(pluginRoot)) yield break;

        foreach (var path in Directory.EnumerateFiles(pluginRoot, "*.dll.disable", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var info = TryProbeDisabledPluginInfo(pluginManager, path);
            if (info is not null)
            {
                yield return new PluginListItem(
                    info.Id,
                    info.Name,
                    info.Version,
                    false,
                    string.IsNullOrWhiteSpace(info.Author) ? "Unknown" : info.Author,
                    info.GithubRepo,
                    info.Description ?? string.Empty,
                    info.Category.ToString());
                continue;
            }

            var enabledFileName = Path.GetFileNameWithoutExtension(path);
            var id = Path.GetFileNameWithoutExtension(enabledFileName);
            if (id.StartsWith("ShiroBot.", StringComparison.OrdinalIgnoreCase)) id = id["ShiroBot.".Length..];
            if (string.IsNullOrWhiteSpace(id)) continue;

            yield return new PluginListItem(
                id,
                id,
                string.Empty,
                false,
                "Unknown",
                null,
                string.Empty,
                "Other");
        }
    }

    private static IEnumerable<PluginListItem> EnumerateUnloadedPlugins(PluginManager pluginManager)
    {
        var pluginRootPath = pluginManager.PluginRootPath;
        var pluginRoot = Path.GetFullPath(pluginRootPath);
        if (!Directory.Exists(pluginRoot)) yield break;

        foreach (var path in PluginManager.EnumeratePluginEntryAssemblies(pluginRoot))
        {
            var info = pluginManager.TryProbePluginInfoFile(path);
            if (info is null) continue;

            yield return new PluginListItem(
                info.Id,
                info.Name,
                info.Version,
                false,
                string.IsNullOrWhiteSpace(info.Author) ? "Unknown" : info.Author,
                info.GithubRepo,
                info.Description ?? string.Empty,
                info.Category.ToString());
        }
    }

    private static PluginProbeInfo? TryProbeDisabledPluginInfo(PluginManager pluginManager, string disabledPath)
    {
        var tempEnabledPath = disabledPath[..^DisabledPluginSuffix.Length];
        return pluginManager.TryProbePluginInfoFile(disabledPath)
               ?? pluginManager.TryProbePluginInfoFile(tempEnabledPath);
    }

    private static bool NameMatches(string? candidate, HashSet<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (aliases.Contains(candidate)) return true;
        return candidate.StartsWith("ShiroBot.", StringComparison.OrdinalIgnoreCase) && aliases.Contains(candidate["ShiroBot.".Length..]);
    }

    private static string GetDisabledPluginPath(string assemblyPath) => assemblyPath + DisabledPluginSuffix;

    private static IEnumerable<string> GetPluginIdAliases(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) yield break;

        yield return id;
        const string shiroBotPrefix = "ShiroBot.";
        if (id.StartsWith(shiroBotPrefix, StringComparison.OrdinalIgnoreCase) && id.Length > shiroBotPrefix.Length)
        {
            yield return id[shiroBotPrefix.Length..];
        }
        else
        {
            yield return shiroBotPrefix + id;
        }
    }

    private static void DeletePluginPath(string pluginRootPath, string targetPath, string pluginName)
    {
        var pluginRoot = Path.GetFullPath(pluginRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!fullTargetPath.StartsWith(pluginRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetDirectoryName(fullTargetPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), pluginRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝删除插件目录之外的文件");
        }

        var parentDirectory = Path.GetDirectoryName(fullTargetPath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException("无法解析插件文件目录");
        }

        var normalizedParent = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullTargetPath);
        var parentName = new DirectoryInfo(normalizedParent).Name;
        var isPluginSubDirectory = !string.Equals(normalizedParent, pluginRoot, StringComparison.OrdinalIgnoreCase) &&
                                   (string.Equals(parentName, pluginName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(parentName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));

        if (isPluginSubDirectory)
        {
            Directory.Delete(normalizedParent, recursive: true);
            return;
        }

        File.Delete(fullTargetPath);
    }

    private static PluginUploadPackage PreparePluginUploadPackage(PluginManager pluginManager, string packagePath)
    {
        var extension = Path.GetExtension(packagePath);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var info = pluginManager.TryProbePluginInfoFile(packagePath)
                       ?? throw new InvalidOperationException("未找到 BotPluginAttribute，文件不是有效插件。");
            return new PluginUploadPackage(Path.GetDirectoryName(packagePath)!, packagePath, "dll", info);
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只支持上传 .dll 或 .zip 插件。");
        }

        var extractRoot = Path.Combine(Path.GetDirectoryName(packagePath)!, "extract");
        Directory.CreateDirectory(extractRoot);
        ExtractZipSafely(packagePath, extractRoot);

        var pluginDlls = Directory.EnumerateFiles(extractRoot, "*.dll", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Info = pluginManager.TryProbePluginInfoFile(path) })
            .Where(item => item.Info is not null)
            .ToArray();

        var package = pluginDlls.Length switch
        {
            0 => throw new InvalidOperationException("压缩包中未找到有效插件 DLL。"),
            > 1 => throw new InvalidOperationException("压缩包中包含多个插件入口 DLL，请一次只上传一个插件。"),
            _ => new PluginUploadPackage(Path.GetDirectoryName(packagePath)!, pluginDlls[0].Path, "zip", pluginDlls[0].Info!)
        };
        var sourceRoot = GetZipInstallSourceRoot(package.RootPath, package.EntryAssemblyPath);
        var relativeEntry = Path.GetRelativePath(sourceRoot, package.EntryAssemblyPath);
        if (relativeEntry.Contains(Path.DirectorySeparatorChar) || relativeEntry.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("压缩包的插件入口 DLL 必须位于包根目录或唯一的顶层文件夹中。");
        }

        return package;
    }

    private static PluginUploadPackage LoadPreparedPluginUploadPackage(PluginManager pluginManager, string uploadId)
    {
        var uploadRoot = GetPluginUploadRoot(uploadId);
        var packagePath = Directory.EnumerateFiles(uploadRoot, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => !Path.GetFileName(path).Equals("extract", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new InvalidOperationException("上传文件不存在。上传可能已过期，请重新上传。");
        }

        return PreparePluginUploadPackage(pluginManager, packagePath);
    }

    private static string InstallUploadedPlugin(PluginManager pluginManager, PluginUploadPackage package)
    {
        var pluginRootPath = pluginManager.PluginRootPath;
        Directory.CreateDirectory(pluginRootPath);
        var targetRoot = GetPluginInstallDirectory(pluginRootPath, package.Info.Id);
        pluginManager.SuppressWatcherPath(targetRoot);
        if (package.Type.Equals("dll", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(targetRoot);
            var targetPath = Path.Combine(targetRoot, Path.GetFileName(package.EntryAssemblyPath));
            // The caller loads the plugin itself; without this the file watcher
            // would queue a second, redundant load for the same assembly.
            pluginManager.SuppressWatcherPath(targetPath);
            File.Copy(package.EntryAssemblyPath, targetPath, overwrite: true);
            pluginManager.SuppressWatcherPath(targetPath);
            pluginManager.SuppressWatcherPath(targetRoot);
            return targetPath;
        }

        var sourceRoot = GetZipInstallSourceRoot(package.RootPath, package.EntryAssemblyPath);
        CopyDirectory(sourceRoot, targetRoot);
        // Restart the window so it is measured from the last write, not the first.
        pluginManager.SuppressWatcherPath(targetRoot);
        return Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, package.EntryAssemblyPath));
    }

    private static string GetPluginInstallDirectory(string pluginRootPath, string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId is "." or ".." ||
            pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            pluginId.Contains(Path.DirectorySeparatorChar) || pluginId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("插件 ID 不能用于安装目录名。");
        }

        return Path.Combine(pluginRootPath, pluginId);
    }

    private static string GetZipInstallSourceRoot(string uploadRoot, string entryAssemblyPath)
    {
        var extractRoot = Path.Combine(uploadRoot, "extract");
        var relativeEntry = Path.GetRelativePath(extractRoot, entryAssemblyPath);
        var firstSeparator = relativeEntry.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (firstSeparator <= 0) return extractRoot;

        var firstSegment = relativeEntry[..firstSeparator];
        var topLevelDirectories = Directory.EnumerateDirectories(extractRoot).Select(Path.GetFileName).ToArray();
        var topLevelFiles = Directory.EnumerateFiles(extractRoot).ToArray();
        return topLevelDirectories.Length == 1 && topLevelFiles.Length == 0 &&
               string.Equals(topLevelDirectories[0], firstSegment, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(extractRoot, firstSegment)
            : extractRoot;
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        var normalizedDestination = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxPluginArchiveEntries)
        {
            throw new InvalidOperationException($"压缩包文件数量超过限制: {archive.Entries.Count} > {MaxPluginArchiveEntries}");
        }

        long totalUncompressedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxPluginExtractedBytes - totalUncompressedBytes)
            {
                throw new InvalidOperationException($"压缩包解压后大小超过限制: {MaxPluginExtractedBytes}");
            }
            totalUncompressedBytes += entry.Length;

            var destinationPath = Path.GetFullPath(Path.Combine(normalizedDestination, entry.FullName));
            if (!destinationPath.StartsWith(normalizedDestination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destinationPath, normalizedDestination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("压缩包包含非法路径。" );
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, directory)));
        }

        Directory.CreateDirectory(targetRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var targetFile = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static string GetPluginUploadRoot(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || uploadId.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
        {
            throw new InvalidOperationException("upload_id 无效。" );
        }

        return Path.Combine(Path.GetTempPath(), "ShiroBot", "plugin_uploads", uploadId);
    }

    private static string GetAdapterUploadRoot(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || uploadId.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
            throw new InvalidOperationException("upload_id 无效。");
        return Path.Combine(Path.GetTempPath(), "ShiroBot", "adapter_uploads", uploadId);
    }

    private static bool TryNormalizeGitHubRepository(string? input, out string repository)
    {
        repository = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

            value = uri.AbsolutePath.Trim('/');
            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        }

        value = value.Trim('/');
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (parts.Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;

        repository = string.Join('/', parts);
        return true;
    }

    private static bool TryNormalizeGitHubAssetUrl(
        string? assetUrl,
        string? requestedAssetName,
        string? repository,
        out string normalizedUrl,
        out string assetName)
    {
        normalizedUrl = string.Empty;
        assetName = string.Empty;
        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(repository))
        {
            var expectedPrefix = "/" + repository.Trim('/') + "/releases/download/";
            if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        }

        var urlAssetName = Path.GetFileName(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(requestedAssetName) &&
            !string.Equals(Path.GetFileName(requestedAssetName), urlAssetName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        assetName = urlAssetName;
        var extension = Path.GetExtension(assetName);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static bool TryNormalizeSha256(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var digest = value.Trim();
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) digest = digest[7..];
        if (digest.Length != 64 || digest.Any(ch => !char.IsAsciiHexDigit(ch))) return false;

        normalized = digest.ToLowerInvariant();
        return true;
    }

    private static IReadOnlyCollection<MarketplaceInstalledPlugin> GetMarketplaceInstalledPlugins(PluginManager pluginManager)
    {
        var plugins = pluginManager.GetLoadedPluginSnapshot()
            .Select(plugin => new MarketplaceInstalledPlugin(plugin.Name, plugin.GithubRepo, plugin.Version, true))
            .Concat(EnumerateDisabledPlugins(pluginManager)
                .Select(plugin => new MarketplaceInstalledPlugin(plugin.Id, plugin.Repo, plugin.Version, false)))
            .Concat(EnumerateUnloadedPlugins(pluginManager)
                .Select(plugin => new MarketplaceInstalledPlugin(plugin.Id, plugin.Repo, plugin.Version, false)));

        return plugins
            .GroupBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static object CreatePluginInfoResponse(PluginProbeInfo info) => new
    {
        id = info.Id,
        name = info.Name,
        version = info.Version,
        author = string.IsNullOrWhiteSpace(info.Author) ? "Unknown" : info.Author,
        repo = info.GithubRepo,
        description = info.Description ?? string.Empty,
        category = info.Category.ToString()
    };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static SemaphoreSlim GetPluginOperationLock(string id)
    {
        var key = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim().ToUpperInvariant();
        return PluginOperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    private static void SchedulePluginUploadCleanup(string uploadRoot)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(PluginUploadTtl).ConfigureAwait(false);
            TryDeleteDirectory(uploadRoot);
        });
    }

    private static void ScheduleAdapterUploadCleanup(string uploadRoot) => SchedulePluginUploadCleanup(uploadRoot);

}
