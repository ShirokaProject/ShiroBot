using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShiroBot.Adapters;

internal sealed class AdapterMarketplaceCache
{
    internal static readonly string MarketplaceUrl =
        "https://raw.githubusercontent.com/ShirokaProject/awesome-shirobot/automation/refresh-marketplace/dist/adapters.v1.json";

    /// <summary>Same lifetime as the plugin market: opening the adapters page reuses it, 检查更新 skips it.</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private JsonArray? _cache;
    private DateTimeOffset _cachedAt;

    /// <summary>The one cache the Dashboard, the console and automatic updates share.</summary>
    public static AdapterMarketplaceCache Shared { get; } = new();

    public Task<JsonArray> GetAsync(CancellationToken cancellationToken) => GetAsync(cancellationToken, false);

    /// <param name="forceRefresh">Fetch the catalog again even when the cached copy is still fresh.</param>
    /// <returns>A copy the caller may change; empty when the market was never reachable.</returns>
    public async Task<JsonArray> GetAsync(CancellationToken cancellationToken, bool forceRefresh)
    {
        if (!forceRefresh && TryGetFresh() is { } fresh) return fresh;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && TryGetFresh() is { } cached) return cached;
            try
            {
                using var response = await Client.GetAsync(MarketplaceUrl, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var document =
                    JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                        as JsonObject;
                _cache = document?["adapters"] as JsonArray ?? [];
                _cachedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested &&
                ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Keep the last catalog that loaded rather than showing an empty market.
                if (_cache is null) return [];
            }

            return (JsonArray)_cache.DeepClone();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private JsonArray? TryGetFresh()
    {
        var cache = _cache;
        return cache is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration
            ? (JsonArray)cache.DeepClone()
            : null;
    }
}
