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
using ShiroBot.Components.Updates;
using static ShiroBot.Update.PluginUpdateService;

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
                    info.Category.ToString(),
                    "disabled");
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
                "Other",
                "disabled");
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
                info.Category.ToString(),
                pluginManager.GetPluginLoadError(info.Id) is { } error ? "error" : "disabled",
                pluginManager.GetPluginLoadError(info.Id));
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

    private static (string Path, bool Directory) GetPluginDeleteTarget(string pluginRootPath, string targetPath, string pluginName)
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
            return (normalizedParent, true);
        }

        return (fullTargetPath, false);
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
        return PluginUpdateService.GetOperationLock(key);
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
