using System.Text.Json.Nodes;

namespace ShiroBot.Adapters;

internal sealed class AdapterMarketplaceCache
{
    internal const string MarketplaceUrl = "https://raw.githubusercontent.com/ShirokaProject/awesome-shirobot/automation/refresh-marketplace/dist/adapters.v1.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<JsonArray> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(MarketplaceUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];
            var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject;
            return document?["adapters"]?.AsArray() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
