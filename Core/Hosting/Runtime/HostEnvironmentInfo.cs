using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ShiroBot.Hosting.Runtime;

/// <summary>
/// Where and how the host process runs, for the Dashboard's 运行环境 card and diagnostics.
/// Facts that cannot be determined are left out instead of guessed.
/// </summary>
internal static class HostEnvironmentInfo
{
    private static readonly Lazy<StaticFacts> Facts = new(ReadStaticFacts);

    /// <summary>docker, systemd or native.</summary>
    public static string RunMode => Facts.Value.Mode;

    public static object Create()
    {
        var facts = Facts.Value;
        return new
        {
            mode = facts.Mode,
            os = facts.Os,
            arch = facts.Arch,
            version_tag = facts.VersionTag,
            framework = RuntimeInformation.FrameworkDescription,
            memory_bytes = Environment.WorkingSet,
            gc_heap_bytes = GC.GetTotalMemory(forceFullCollection: false),
            build_time = facts.BuildTime
        };
    }

    private sealed record StaticFacts(string Mode, string Os, string Arch, string? VersionTag, string? BuildTime);

    private static StaticFacts ReadStaticFacts() => new(
        DetectRunMode(),
        DescribeOperatingSystem(),
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        NonEmpty(Environment.GetEnvironmentVariable("SHIROBOT_VERSION_TAG")),
        NormalizeTimestamp(Environment.GetEnvironmentVariable("SHIROBOT_BUILD_TIME"))
            ?? NormalizeTimestamp(typeof(HostEnvironmentInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "ShiroBot.BuildTimeUtc")?.Value));

    private static string DetectRunMode()
    {
        // The official .NET container images set DOTNET_RUNNING_IN_CONTAINER; the marker files cover others.
        if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
            File.Exists("/.dockerenv") || File.Exists("/run/.containerenv"))
            return "docker";

        // systemd sets INVOCATION_ID for every unit it starts.
        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID")))
            return "systemd";

        return "native";
    }

    private static string DescribeOperatingSystem()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var line in File.ReadLines("/etc/os-release"))
                {
                    if (!line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal)) continue;
                    var name = line["PRETTY_NAME=".Length..].Trim().Trim('"');
                    if (name.Length > 0) return name;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fall back to the kernel description below.
            }
        }

        if (OperatingSystem.IsMacOS())
            return $"macOS {Environment.OSVersion.Version.ToString(3)}";

        var description = RuntimeInformation.OSDescription.Trim();
        return description.StartsWith("Microsoft ", StringComparison.Ordinal) ? description["Microsoft ".Length..] : description;
    }

    private static string? NormalizeTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : null;

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
