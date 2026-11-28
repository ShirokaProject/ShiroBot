using System.Text.Json.Nodes;

namespace ShiroBot.Components.Updates;

/// <summary>
/// The installable release of a plugin or adapter market entry. Both markets publish the same shape:
/// <c>release.version</c>, <c>release.prerelease</c> and <c>release.asset</c> with url, name and a sha256 digest.
/// </summary>
internal sealed record MarketRelease(string Version, bool Prerelease, string AssetUrl, string AssetName, string? Sha256)
{
    /// <summary>The entry's release, or null when it has no downloadable asset.</summary>
    public static MarketRelease? From(JsonObject entry)
    {
        if (entry["release"] is not JsonObject release ||
            release["version"]?.GetValue<string>() is not { Length: > 0 } version ||
            release["asset"] is not JsonObject asset ||
            asset["url"]?.GetValue<string>() is not { Length: > 0 } url ||
            asset["name"]?.GetValue<string>() is not { Length: > 0 } name)
            return null;

        var digest = asset["digest"]?.GetValue<string>();
        var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest["sha256:".Length..] : null;
        return new MarketRelease(version, release["prerelease"]?.GetValue<bool>() == true, url, name, sha256);
    }
}
