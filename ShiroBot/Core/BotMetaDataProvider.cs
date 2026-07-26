using System.Reflection;
using ShiroBot.SDK.Models;

namespace ShiroBot.Core;

internal static class BotMetaDataProvider
{
    private static string BotVersion => GetVersion(typeof(Program).Assembly);

    private static string SdkVersion => GetVersion(typeof(BotEvent).Assembly);

    private static string CommitShortHash
    {
        get
        {
            var informationalVersion = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            var parts = informationalVersion?.Split('+', 2);
            var commit = parts?.Length == 2 ? parts[1] : null;

            return !string.IsNullOrWhiteSpace(commit)
                ? commit[..Math.Min(6, commit.Length)]
                : "unknown";
        }
    }

    internal static string StartupVersionText =>
        $"当前 ShiroBot 版本: {BotVersion} | SDK 版本: {SdkVersion} | Commit: {CommitShortHash}";

    private static string GetVersion(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString(3)
                      ?? "unknown";

        return version.Split('+', 2)[0];
    }
}