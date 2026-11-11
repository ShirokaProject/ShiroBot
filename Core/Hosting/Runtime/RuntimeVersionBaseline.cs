using System.Reflection;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Hosting.Runtime;

internal static class RuntimeVersionBaseline
{
    internal static Version BuildVersion { get; } = Version.Parse(typeof(RuntimeVersionBaseline).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "ShiroBot.BuildRuntimeVersion").Value!);

    internal static string? GetUpgradeMessage(Version current, Version baseline)
    {
        return current < baseline
            ? $"当前 .NET Runtime {current} 低于此构建的运行时基线 {baseline}。宿主将继续运行，请升级 .NET Runtime（ASP.NET Core Runtime）至 {baseline} 或更高的同系列版本。"
            : null;
    }

    internal static void ReportIfOlder()
    {
        if (GetUpgradeMessage(Environment.Version, BuildVersion) is { } message)
            BotLog.Error(message);
    }
}
