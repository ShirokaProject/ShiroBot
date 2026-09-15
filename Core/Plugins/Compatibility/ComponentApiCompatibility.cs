using ShiroBot.SDK.Core;

namespace ShiroBot.Plugins.Compatibility;

internal static class ComponentApiCompatibility
{
    public static void EnsureCompatible(string kind, string id, string minimumApiVersion, string maximumApiVersion)
    {
        if (!Version.TryParse(minimumApiVersion, out var minimumVersion) ||
            !Version.TryParse(maximumApiVersion, out var maximumVersion) ||
            minimumVersion > maximumVersion)
        {
            throw new InvalidOperationException(
                $"{kind} {id} declares invalid ShiroBot API range {minimumApiVersion}..{maximumApiVersion}.");
        }

        var currentVersion = Version.Parse(ShiroBotApi.CurrentVersion);
        if (currentVersion < minimumVersion)
        {
            throw new InvalidOperationException(
                $"{kind} {id} requires ShiroBot API {minimumApiVersion} or later " +
                $"(tested through {maximumApiVersion}), but this host implements API {ShiroBotApi.CurrentVersion}.");
        }
    }
}
