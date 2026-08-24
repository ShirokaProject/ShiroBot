using ShiroBot.SDK.Core;

namespace ShiroBot.Core;

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
        if (currentVersion < minimumVersion || currentVersion > maximumVersion)
        {
            throw new InvalidOperationException(
                $"{kind} {id} supports ShiroBot API {minimumApiVersion}..{maximumApiVersion}, " +
                $"but this host implements API {ShiroBotApi.CurrentVersion}.");
        }
    }
}
