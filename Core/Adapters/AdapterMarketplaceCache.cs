using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShiroBot.Adapters;

internal sealed class AdapterMarketplaceCache
{
    internal static readonly string MarketplaceUrl =
        "https://raw.githubusercontent.com/ShirokaProject/awesome-shirobot/automation/refresh-marketplace/dist/adapters.v1.json";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>The one cache the Dashboard, the console and automatic updates share.</summary>
    public static AdapterMarketplaceCache Shared { get; } = new();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Keep the existing instance service API compatible with compiled callers.")]
    public async Task<JsonArray> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(MarketplaceUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];
            var document =
                JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as
                    JsonObject;
            return document?["adapters"]?.AsArray() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }
}