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

    public static void Initialize(
        Func<IReadOnlyList<long>> getOwnerIds,
        Func<long, string, Task> sendPrivateMessageAsync)
    {
        _getOwnerIds = getOwnerIds;
        _sendPrivateMessageAsync = sendPrivateMessageAsync;
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
            release.Body);
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
            async () => await UpdatePluginAsync(request.PluginName, cancellationToken));

        await NotifyOwnersAsync(
            $"检测到插件更新: {request.PluginName} {request.CurrentVersion} -> {request.LatestVersion}\n" +
            (string.IsNullOrWhiteSpace(request.ReleaseUrl) ? string.Empty : $"来源: {request.ReleaseUrl}\n") +
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

    public static Task UpdatePluginAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
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
        var releases = await GetReleasesAsync(repository, cancellationToken);
        if (releases.Count == 0)
        {
            return null;
        }

        var release = includePrerelease
            ? releases.FirstOrDefault()
            : releases.FirstOrDefault(item => !item.Prerelease && !item.Draft);

        return release;
    }

    private static async Task<List<GitHubRelease>> GetReleasesAsync(string repository, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository}/releases?per_page=20");

        request.Headers.UserAgent.ParseAdd("ShiroBot-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var releases = new List<GitHubRelease>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            releases.Add(new GitHubRelease(
                item.GetPropertyOrDefault("tag_name"),
                item.GetPropertyOrDefault("name"),
                item.GetPropertyOrDefault("html_url"),
                item.GetPropertyOrDefault("body"),
                item.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean(),
                item.TryGetProperty("draft", out var draft) && draft.GetBoolean()));
        }

        return releases;
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
        bool Draft);

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
