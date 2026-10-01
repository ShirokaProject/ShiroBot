using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Update;

public sealed record GitHubPluginPackage(
    string Repository,
    string Version,
    string ReleaseName,
    string ReleaseUrl,
    string Body,
    string? AssetDownloadUrl,
    string? AssetName,
    string? AssetType);

public static class Updater
{
    internal static HttpClient HttpClient { get; set; } = new();
    private static readonly ConcurrentDictionary<string, PendingUpdateEntry> PendingUpdates = new(StringComparer.OrdinalIgnoreCase);
    private static Func<IReadOnlyList<string>> _getOwnerIds = () => [];
    private static Func<string, string, Task> _sendPrivateMessageAsync = (_, _) => Task.CompletedTask;
    private static string? _githubProxy;

    public static void Initialize(
        Func<IReadOnlyList<string>> getOwnerIds,
        Func<string, string, Task> sendPrivateMessageAsync,
        string? githubProxy = null)
    {
        _getOwnerIds = getOwnerIds;
        _sendPrivateMessageAsync = sendPrivateMessageAsync;
        _githubProxy = githubProxy;
    }

    public static async Task<GitHubReleaseUpdate?> CheckGitHubReleaseAsync(
        string repository,
        string currentVersion,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository cannot be empty.", nameof(repository));
        }

        var release = await GetLatestReleaseAsync(repository, includePrerelease, cancellationToken);
        if (release is null)
        {
            return null;
        }

        if (!IsNewerVersion(release.TagName, currentVersion))
        {
            return null;
        }

        return new GitHubReleaseUpdate(
            repository,
            NormalizeVersion(currentVersion),
            NormalizeVersion(release.TagName),
            release.Name,
            release.HtmlUrl,
            release.Body,
            release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))?.DownloadUrl,
            release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))?.Name);
    }

    public static async Task<GitHubReleaseUpdate?> CheckGitHubReleaseAssetAsync(
        string repository,
        string currentVersion,
        string assetName,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository cannot be empty.", nameof(repository));
        }

        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new ArgumentException("Asset name cannot be empty.", nameof(assetName));
        }

        var release = await GetLatestReleaseAsync(repository, includePrerelease, cancellationToken);
        if (release is null || !IsNewerVersion(release.TagName, currentVersion))
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(item => string.Equals(item.Name, assetName, StringComparison.OrdinalIgnoreCase));
        return new GitHubReleaseUpdate(
            repository,
            NormalizeVersion(currentVersion),
            NormalizeVersion(release.TagName),
            release.Name,
            release.HtmlUrl,
            release.Body,
            asset?.DownloadUrl,
            asset?.Name);
    }

    public static async Task<GitHubReleaseUpdate?> CheckGitHubPluginPackageUpdateAsync(
        string repository,
        string currentVersion,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var package = await GetLatestPluginPackageAsync(repository, includePrerelease, cancellationToken).ConfigureAwait(false);
        if (package is null || !IsNewerVersion(package.Version, currentVersion)) return null;

        return new GitHubReleaseUpdate(
            package.Repository,
            NormalizeVersion(currentVersion),
            package.Version,
            package.ReleaseName,
            package.ReleaseUrl,
            package.Body,
            package.AssetDownloadUrl,
            package.AssetName);
    }

    public static async Task<GitHubPluginPackage?> GetLatestPluginPackageAsync(
        string repository,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository cannot be empty.", nameof(repository));
        }

        var release = await GetLatestReleaseAsync(repository, includePrerelease, cancellationToken);
        if (release is null)
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ?? release.Assets.FirstOrDefault(item => item.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return new GitHubPluginPackage(
                repository,
                NormalizeVersion(release.TagName),
                release.Name,
                release.HtmlUrl,
                release.Body,
                null,
                null,
                null);
        }

        return new GitHubPluginPackage(
            repository,
            NormalizeVersion(release.TagName),
            release.Name,
            release.HtmlUrl,
            release.Body,
            asset.DownloadUrl,
            asset.Name,
            Path.GetExtension(asset.Name).TrimStart('.').ToLowerInvariant());
    }

    /// <summary>
    /// Installs a host release and restarts. Set at startup so console, plugin and Dashboard requests all
    /// replace the executable the same way.
    /// </summary>
    internal static Func<string, CancellationToken, Task>? HostUpdateApplier { get; set; }

    public static async Task<string> RequestHostUpdateAsync(
        GitHubReleaseUpdate update,
        CancellationToken cancellationToken = default)
    {
        var id = CreatePendingUpdate(
            UpdateTarget.Host,
            "宿主",
            update.CurrentVersion,
            update.LatestVersion,
            update.ReleaseUrl,
            async token =>
            {
                if (HostUpdateApplier is { } apply && !string.IsNullOrWhiteSpace(update.AssetDownloadUrl))
                    await apply(update.AssetDownloadUrl, token).ConfigureAwait(false);
                else
                    throw new InvalidOperationException("宿主更新服务尚未初始化或缺少下载地址。");
            });

        await NotifyOwnersAsync(
            $"检测到宿主更新: {update.CurrentVersion} -> {update.LatestVersion}\n" +
            $"来源: {update.Repository}\n" +
            (string.IsNullOrWhiteSpace(update.AssetName) ? string.Empty : $"文件: {update.AssetName}\n") +
            $"确认更新: update confirm {id}\n" +
            $"取消更新: update cancel {id}",
            cancellationToken);

        return id;
    }

    internal static Func<PluginUpdateRequest, CancellationToken, Task>? PluginUpdateApplier { get; set; }

    public static async Task<string> RequestPluginUpdateAsync(
        PluginUpdateRequest request,
        CancellationToken cancellationToken = default)
        => await RequestPluginUpdateAsync(
            request,
            token => PluginUpdateApplier is { } apply
                ? apply(request, token)
                : throw new InvalidOperationException("插件更新服务尚未初始化。"),
            cancellationToken).ConfigureAwait(false);

    internal static async Task<string> RequestPluginUpdateAsync(
        PluginUpdateRequest request,
        Func<CancellationToken, Task> executeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);
        var id = CreatePendingUpdate(
            UpdateTarget.Plugin,
            request.PluginName,
            request.CurrentVersion,
            request.LatestVersion,
            request.ReleaseUrl,
            executeAsync);

        await NotifyOwnersAsync(
            $"检测到插件更新: {request.PluginName} {request.CurrentVersion} -> {request.LatestVersion}\n" +
            (string.IsNullOrWhiteSpace(request.ReleaseUrl) ? string.Empty : $"来源: {request.ReleaseUrl}\n") +
            (string.IsNullOrWhiteSpace(request.AssetDownloadUrl) ? string.Empty : $"文件: {request.AssetDownloadUrl}\n") +
            $"确认更新: update confirm {id}\n" +
            $"取消更新: update cancel {id}",
            cancellationToken);

        return id;
    }

    public static IReadOnlyList<PendingUpdateInfo> GetPendingUpdates()
    {
        return PendingUpdates.Values
            .OrderBy(item => item.CreatedAt)
            .Select(item => new PendingUpdateInfo(
                item.Id,
                item.Target,
                item.Name,
                item.CurrentVersion,
                item.LatestVersion,
                item.ReleaseUrl))
            .ToList();
    }

    public static async Task<bool> ConfirmUpdateAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (!PendingUpdates.TryRemove(requestId, out var entry))
        {
            return false;
        }

        try
        {
            if (entry.Target == UpdateTarget.Host)
                await NotifyOwnersAsync($"宿主更新即将执行并重启: {entry.Name} ({entry.Id})", cancellationToken);
            await entry.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            PendingUpdates.TryAdd(requestId, entry);
            throw;
        }
        if (entry.Target == UpdateTarget.Host) return true;

        await NotifyOwnersAsync($"更新任务已执行: {entry.Name} ({entry.Id})", cancellationToken);
        return true;
    }

    public static bool CancelUpdate(string requestId)
    {
        return PendingUpdates.TryRemove(requestId, out _);
    }

    public static Task UpdateSelfAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("请先检查宿主版本，再确认包含下载地址的更新请求。"));

    public static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        string? expectedSha256 = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApplyGithubProxy(url));
        request.Headers.UserAgent.ParseAdd("ShiroBot-Updater");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (maxBytes is { } limit && response.Content.Headers.ContentLength is { } contentLength && contentLength > limit)
        {
            throw new InvalidOperationException($"下载文件超过大小限制: {contentLength} > {limit}");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            totalBytes += read;
            if (maxBytes is { } maximum && totalBytes > maximum)
            {
                throw new InvalidOperationException($"下载文件超过大小限制: {totalBytes} > {maximum}");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"下载文件 SHA-256 校验失败。Expected {expectedSha256}, actual {actualSha256}。");
            }
        }
    }

    private static bool IsZipPackage(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            Span<byte> header = stackalloc byte[4];
            using var stream = File.OpenRead(path);
            return stream.Read(header) == 4 &&
                   header[0] == 0x50 && header[1] == 0x4B &&
                   header[2] == 0x03 && header[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }

    public static async Task UpdatePluginAsync(string pluginName, string? assetDownloadUrl, string? targetPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetDownloadUrl))
        {
            throw new InvalidOperationException($"插件 {pluginName} 的更新缺少插件包下载地址。");
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"插件 {pluginName} 的更新缺少目标路径。");
        }

        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? AppContext.BaseDirectory;
        var tempDirectory = Path.Combine(targetDirectory, ".tmp");
        Directory.CreateDirectory(tempDirectory);

        var packagePath = Path.Combine(
            tempDirectory,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.package");
        var replacementPath = Path.Combine(
            tempDirectory,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.replacement");
        var backupPath = Path.Combine(
            tempDirectory,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.backup");

        try
        {
            await DownloadFileAsync(assetDownloadUrl, packagePath, cancellationToken);
            if (IsZipPackage(packagePath))
            {
                ExtractPluginEntryFromZip(packagePath, replacementPath, Path.GetFileName(targetPath));
            }
            else
            {
                File.Copy(packagePath, replacementPath, overwrite: true);
            }

            if (File.Exists(targetPath)) File.Copy(targetPath, backupPath, overwrite: true);
            try
            {
                File.Move(replacementPath, targetPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(backupPath)) File.Copy(backupPath, targetPath, overwrite: true);
                throw;
            }
        }
        finally
        {
            try
            {
                if (File.Exists(packagePath)) File.Delete(packagePath);
                if (File.Exists(replacementPath)) File.Delete(replacementPath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
                DeleteDirectoryIfEmpty(tempDirectory);
            }
            catch
            {
                // ignored: best-effort cleanup
            }
        }
    }

    internal static void ExtractPluginEntryFromZip(string packagePath, string destinationPath, string targetFileName)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var exactMatches = archive.Entries
            .Where(entry => string.Equals(Path.GetFileName(entry.FullName), targetFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matches = exactMatches.Length > 0
            ? exactMatches
            : archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                                              !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"插件更新包中未找到唯一入口 DLL: {targetFileName}");

        var entry = matches[0];
        if (entry.Length <= 0) throw new InvalidOperationException($"插件更新包中的入口 DLL 为空: {entry.FullName}");
        using var input = entry.Open();
        using var output = File.Create(destinationPath);
        input.CopyTo(output);
    }

    private static void DeleteDirectoryIfEmpty(string directory)
    {
        if (!Directory.Exists(directory)) return;
        if (Directory.EnumerateFileSystemEntries(directory).Any()) return;
        Directory.Delete(directory);
    }

    private static string CreatePendingUpdate(
        UpdateTarget target,
        string name,
        string currentVersion,
        string latestVersion,
        string? releaseUrl,
        Func<CancellationToken, Task> action)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        PendingUpdates[id] = new PendingUpdateEntry(
            id,
            target,
            name,
            NormalizeVersion(currentVersion),
            NormalizeVersion(latestVersion),
            releaseUrl,
            action);
        return id;
    }

    private static async Task NotifyOwnersAsync(string content, CancellationToken cancellationToken)
    {
        var ownerIds = _getOwnerIds();
        foreach (var ownerId in ownerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _sendPrivateMessageAsync(ownerId, content);
        }
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync(
        string repository,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        return await GetLatestReleaseFromApiAsync(repository, includePrerelease, cancellationToken);
    }

    private static async Task<GitHubRelease?> GetLatestReleaseFromApiAsync(
        string repository,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            includePrerelease
                ? $"https://api.github.com/repos/{repository}/releases?per_page=20"
                : $"https://api.github.com/repos/{repository}/releases/latest");

        request.Headers.UserAgent.ParseAdd("ShiroBot-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var item = includePrerelease
            ? document.RootElement.EnumerateArray().FirstOrDefault(release =>
                !release.TryGetProperty("draft", out var releaseDraft) || !releaseDraft.GetBoolean())
            : document.RootElement;
        if (item.ValueKind != JsonValueKind.Object) return null;

        return new GitHubRelease(
            item.GetPropertyOrDefault("tag_name"),
            item.GetPropertyOrDefault("name"),
            item.GetPropertyOrDefault("html_url"),
            item.GetPropertyOrDefault("body"),
            GetReleaseAssets(item));
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        var normalizedLatest = NormalizeVersion(latestVersion);
        var normalizedCurrent = NormalizeVersion(currentVersion);

        if (Version.TryParse(normalizedLatest, out var latest) && Version.TryParse(normalizedCurrent, out var current))
        {
            return latest > current;
        }

        return !string.Equals(normalizedLatest, normalizedCurrent, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version)
    {
        var trimmed = version.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? trimmed[1..]
            : trimmed;
    }

    private sealed record GitHubRelease(
        string TagName,
        string Name,
        string HtmlUrl,
        string Body,
        IReadOnlyList<GitHubReleaseAsset> Assets);

    private sealed record GitHubReleaseAsset(string Name, string DownloadUrl);

    private static IReadOnlyList<GitHubReleaseAsset> GetReleaseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<GitHubReleaseAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetPropertyOrDefault("name");
            var downloadUrl = asset.GetPropertyOrDefault("browser_download_url");
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                results.Add(new GitHubReleaseAsset(name, downloadUrl));
            }
        }

        return results;
    }

    private sealed record PendingUpdateEntry(
        string Id,
        UpdateTarget Target,
        string Name,
        string CurrentVersion,
        string LatestVersion,
        string? ReleaseUrl,
        Func<CancellationToken, Task> Execute)
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Execute(cancellationToken);
        }
    }

    private static string ApplyGithubProxy(string url)
    {
        if (string.IsNullOrWhiteSpace(_githubProxy)) return url;

        return _githubProxy.TrimEnd('/') + "/" + url;
    }
}

internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
