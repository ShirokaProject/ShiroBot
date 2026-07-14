using System.Text.Json.Nodes;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Hosting;

internal sealed record MarketplaceInstalledPlugin(
    string Id,
    string? Repository,
    string Version,
    bool Enabled);

internal sealed class PluginMarketplaceCache
{
    private const string MarketplaceUrl =
        "https://raw.githubusercontent.com/ShirokaProject/awesome-shirobot/automation/refresh-marketplace/dist/marketplace.v1.json";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private JsonObject? _memoryCache;
    private DateTimeOffset _memoryCachedAt;

    public async Task<JsonObject> GetAsync(
        IReadOnlyCollection<MarketplaceInstalledPlugin> installedPlugins,
        CancellationToken cancellationToken)
    {
        var marketplace = await GetMarketplaceAsync(cancellationToken).ConfigureAwait(false);
        return AddInstalledState(marketplace, installedPlugins);
    }

    private async Task<JsonObject> GetMarketplaceAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache is not null && DateTimeOffset.UtcNow - _memoryCachedAt < CacheDuration)
        {
            return (JsonObject)_memoryCache.DeepClone();
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_memoryCache is not null && DateTimeOffset.UtcNow - _memoryCachedAt < CacheDuration)
            {
                return (JsonObject)_memoryCache.DeepClone();
            }

            var diskCache = await TryReadDiskCacheAsync(cancellationToken).ConfigureAwait(false);
            if (diskCache.Document is not null && DateTimeOffset.UtcNow - diskCache.CachedAt < CacheDuration)
            {
                SetMemoryCache(diskCache.Document, diskCache.CachedAt);
                return (JsonObject)diskCache.Document.DeepClone();
            }

            try
            {
                using var response = await HttpClient.GetAsync(MarketplaceUrl, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var document = ParseMarketplace(json);
                var cachedAt = DateTimeOffset.UtcNow;
                SetMemoryCache(document, cachedAt);
                await TryWriteDiskCacheAsync(json, cancellationToken).ConfigureAwait(false);
                return (JsonObject)document.DeepClone();
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested &&
                ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
            {
                if (diskCache.Document is not null)
                {
                    BotLog.Warning("插件市场远端不可用，使用本地 last-known-good 缓存: " + ex.Message);
                    SetMemoryCache(diskCache.Document, DateTimeOffset.UtcNow);
                    return (JsonObject)diskCache.Document.DeepClone();
                }

                throw new InvalidOperationException("插件市场当前不可用，且没有本地缓存。", ex);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static JsonObject AddInstalledState(
        JsonObject source,
        IReadOnlyCollection<MarketplaceInstalledPlugin> installedPlugins)
    {
        var plugins = new JsonArray();
        foreach (var node in source["plugins"]?.AsArray() ?? [])
        {
            if (node is not JsonObject sourcePlugin) continue;

            var plugin = (JsonObject)sourcePlugin.DeepClone();
            var id = GetString(plugin, "id") ?? GetString(plugin, "pluginId") ?? GetString(plugin, "stableId");
            var repository = NormalizeRepository(
                GetString(plugin, "repository") ?? GetString(plugin, "githubRepo") ?? GetString(plugin, "repo"));
            var installed = installedPlugins.FirstOrDefault(candidate =>
                (!string.IsNullOrWhiteSpace(id) &&
                 string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                (repository is not null &&
                 string.Equals(NormalizeRepository(candidate.Repository), repository, StringComparison.OrdinalIgnoreCase)));
            if (installed is not null)
            {
                plugin["installed"] = new JsonObject
                {
                    ["version"] = installed.Version,
                    ["enabled"] = installed.Enabled
                };
            }
            else
            {
                plugin.Remove("installed");
            }
            plugins.Add(plugin);
        }

        return new JsonObject
        {
            ["schemaVersion"] = source["schemaVersion"]?.DeepClone(),
            ["generatedAt"] = source["generatedAt"]?.DeepClone(),
            ["plugins"] = plugins
        };
    }

    private static JsonObject ParseMarketplace(string json)
    {
        var document = JsonNode.Parse(json) as JsonObject
                       ?? throw new InvalidOperationException("插件市场响应不是 JSON 对象。");
        if (document["plugins"] is not JsonArray)
        {
            throw new InvalidOperationException("插件市场响应缺少 plugins 数组。");
        }

        return document;
    }

    private static string? GetString(JsonObject obj, string propertyName) =>
        obj[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? NormalizeRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;

        var value = repository.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            value = uri.AbsolutePath;
        }

        value = value.Trim('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? string.Join('/', parts) : null;
    }

    private void SetMemoryCache(JsonObject document, DateTimeOffset cachedAt)
    {
        _memoryCache = (JsonObject)document.DeepClone();
        _memoryCachedAt = cachedAt;
    }

    private static async Task<(JsonObject? Document, DateTimeOffset CachedAt)> TryReadDiskCacheAsync(
        CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath();
        try
        {
            if (!File.Exists(cachePath)) return (null, DateTimeOffset.MinValue);

            var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            return (ParseMarketplace(json), File.GetLastWriteTimeUtc(cachePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            BotLog.Warning("读取插件市场缓存失败: " + ex.Message);
            return (null, DateTimeOffset.MinValue);
        }
    }

    private static async Task TryWriteDiskCacheAsync(string json, CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath();
        var tempPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BotLog.Warning("写入插件市场缓存失败: " + ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string GetCachePath() =>
        Path.Combine(AppContext.BaseDirectory, "cache", "plugin-marketplace.v1.json");
}
