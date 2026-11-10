using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Runtime;

/// <summary>
///     Where and how the host process runs, for the Dashboard's 运行环境 card and diagnostics.
///     Facts that cannot be determined are left out instead of guessed.
/// </summary>
internal static class HostEnvironmentInfo
{
    private static readonly Lazy<StaticFacts> Facts = new(ReadStaticFacts);

    public static string RunMode => Facts.Value.Environment.Mode;
    internal static HostExecutionEnvironment ExecutionEnvironment => Facts.Value.Environment;

    public static object Create()
    {
        var facts = Facts.Value;
        return new
        {
            mode = facts.Environment.Mode,
            is_container = facts.Environment.IsContainer,
            container_engine = facts.Environment.Engine,
            container_runtime = facts.Environment.Sandbox,
            container_host = facts.Environment.Host,
            orchestrator = facts.Environment.Orchestrator,
            os = facts.Os,
            arch = facts.Arch,
            version_tag = facts.VersionTag,
            framework = RuntimeInformation.FrameworkDescription,
            memory_bytes = Environment.WorkingSet,
            gc_heap_bytes = GC.GetTotalMemory(false),
            build_time = facts.BuildTime
        };
    }

    /// <summary>
    ///     The same facts as <see cref="Create" />, for plugins through
    ///     <see cref="ShiroBot.SDK.Plugin.IBotContext.GetHostStatus" />.
    /// </summary>
    public static HostRuntimeInfo CreateRuntimeInfo()
    {
        var facts = Facts.Value;
        return new HostRuntimeInfo
        {
            Mode = facts.Environment.Mode,
            OperatingSystem = facts.Os,
            Architecture = facts.Arch,
            Framework = RuntimeInformation.FrameworkDescription,
            ImageTag = facts.VersionTag,
            BuildTime = DateTimeOffset.TryParse(facts.BuildTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var buildTime)
                ? buildTime
                : null,
            MemoryBytes = Environment.WorkingSet,
            GcHeapBytes = GC.GetTotalMemory(false),
            ProcessorCount = Environment.ProcessorCount
        };
    }

    private static StaticFacts ReadStaticFacts()
    {
        return new StaticFacts(
            HostEnvironmentDetector.Detect(),
            DescribeOperatingSystem(),
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            NonEmpty(Environment.GetEnvironmentVariable("SHIROBOT_VERSION_TAG")),
            NormalizeTimestamp(Environment.GetEnvironmentVariable("SHIROBOT_BUILD_TIME"))
            ?? NormalizeTimestamp(typeof(HostEnvironmentInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "ShiroBot.BuildTimeUtc")?.Value));
    }

    private static string DescribeOperatingSystem()
    {
        if (OperatingSystem.IsLinux())
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

        if (OperatingSystem.IsMacOS())
            return $"macOS {Environment.OSVersion.Version.ToString(3)}";

        var description = RuntimeInformation.OSDescription.Trim();
        return description.StartsWith("Microsoft ", StringComparison.Ordinal)
            ? description["Microsoft ".Length..]
            : description;
    }

    private static string? NormalizeTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NonEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record StaticFacts(
        HostExecutionEnvironment Environment,
        string Os,
        string Arch,
        string? VersionTag,
        string? BuildTime);
}