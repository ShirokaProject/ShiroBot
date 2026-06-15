using System.Collections.Concurrent;
using System.Text.Json;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Core;

public static class Updater
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, PendingUpdateEntry> PendingUpdates = new(StringComparer.OrdinalIgnoreCase);
    private static Func<IReadOnlyList<long>> _getOwnerIds = () => [];
    private static Func<long, string, Task> _sendPrivateMessageAsync = (_, _) => Task.CompletedTask;
    private static string? _githubProxy;

    public static void Initialize(
        Func<IReadOnlyList<long>> getOwnerIds,
        Func<long, string, Task> sendPrivateMessageAsync,
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
            async () => await UpdateSelfAsync(cancellationToken));

        await NotifyOwnersAsync(
            $"检测到宿主更新: {update.CurrentVersion} -> {update.LatestVersion}\n" +
            $"来源: {update.Repository}\n" +
            (string.IsNullOrWhiteSpace(update.AssetName) ? string.Empty : $"文件: {update.AssetName}\n") +
            $"确认更新: update confirm {id}\n" +
            $"取消更新: update cancel {id}",
            cancellationToken);

        return id;
    }

    public static async Task<string> RequestPluginUpdateAsync(
        PluginUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var id = CreatePendingUpdate(
            UpdateTarget.Plugin,
            request.PluginName,
            request.CurrentVersion,
            request.LatestVersion,
            request.ReleaseUrl,
            async () => await UpdatePluginAsync(request.PluginName, request.AssetDownloadUrl, request.TargetPath, cancellationToken));

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

        await entry.ExecuteAsync(cancellationToken);
        await NotifyOwnersAsync($"更新任务已执行: {entry.Name} ({entry.Id})", cancellationToken);
        return true;
    }

    public static bool CancelUpdate(string requestId)
    {
        return PendingUpdates.TryRemove(requestId, out _);
    }

    public static Task UpdateSelfAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public static async Task UpdatePluginAsync(string pluginName, string? assetDownloadUrl, string? targetPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetDownloadUrl))
        {
            throw new InvalidOperationException($"插件 {pluginName} 的更新缺少 DLL 下载地址。");
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"插件 {pluginName} 的更新缺少目标路径。");
        }

        var tempPath = targetPath + ".download";
        using var request = new HttpRequestMessage(HttpMethod.Get, ApplyGithubProxy(assetDownloadUrl));
        request.Headers.UserAgent.ParseAdd("ShiroBot-Updater");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        File.Copy(tempPath, targetPath, overwrite: true);
        File.Delete(tempPath);
    }

    private static string CreatePendingUpdate(
        UpdateTarget target,
        string name,
        string currentVersion,
        string latestVersion,
        string? releaseUrl,
        Func<Task> action)
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
        return await GetLatestReleaseFromApiAsync(repository, cancellationToken);
    }

    private static async Task<GitHubRelease?> GetLatestReleaseFromApiAsync(string repository, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository}/releases/latest");

        request.Headers.UserAgent.ParseAdd("ShiroBot-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var item = document.RootElement;
        return new GitHubRelease(
            item.GetPropertyOrDefault("tag_name"),
            item.GetPropertyOrDefault("name"),
            item.GetPropertyOrDefault("html_url"),
            item.GetPropertyOrDefault("body"),
            item.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean(),
            item.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
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
        bool Prerelease,
        bool Draft,
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
        Func<Task> Execute)
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Execute();
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
