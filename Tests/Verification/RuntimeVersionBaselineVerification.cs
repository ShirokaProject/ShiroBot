using ShiroBot.Hosting.Runtime;

namespace ShiroBot.Verification;

internal static class RuntimeVersionBaselineVerification
{
    internal static void Run()
    {
        var baseline = new Version(11, 0, 5);
        var older = RuntimeVersionBaseline.GetUpgradeMessage(new Version(11, 0, 1), baseline);
        if (older is null || !older.Contains("11.0.1") || !older.Contains("11.0.5") ||
            !older.Contains("继续运行") || !older.Contains(".NET Runtime") ||
            RuntimeVersionBaseline.GetUpgradeMessage(baseline, baseline) is not null ||
            RuntimeVersionBaseline.GetUpgradeMessage(new Version(11, 0, 6), baseline) is not null ||
            RuntimeVersionBaseline.GetUpgradeMessage(new Version(12, 0, 0), baseline) is not null ||
            RuntimeVersionBaseline.BuildVersion.Major != 11)
            throw new InvalidOperationException("Build runtime baseline metadata or upgrade advisory is incorrect.");
        Console.WriteLine("Build runtime baseline metadata and older/equal/newer runtime advisory verification passed.");
    }
}
