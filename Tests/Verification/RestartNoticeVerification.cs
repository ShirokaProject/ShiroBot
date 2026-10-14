using System.Text.Json;
using ShiroBot.Hosting.Runtime;

internal static class RestartNoticeVerification
{
    internal static void Run()
    {
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        using (var empty = JsonDocument.Parse(JsonSerializer.Serialize(runtime.CreateRestartNotice())))
            if (empty.RootElement.GetProperty("recommended").GetBoolean()) throw new Exception("A fresh host must not recommend restart.");
        runtime.RecommendRestart("plugin:Example", "旧文件未清理，建议重启。");
        runtime.RecommendRestart("plugin:Example", "旧文件未清理，建议重启。");
        using (var notice = JsonDocument.Parse(JsonSerializer.Serialize(runtime.CreateRestartNotice())))
        {
            if (!notice.RootElement.GetProperty("recommended").GetBoolean() || notice.RootElement.GetProperty("reasons").GetArrayLength() != 1)
                throw new Exception("Dashboard restart notice must persist and deduplicate the same plugin.");
        }
        for (int i = 0; i < 40; i++) runtime.RecommendRestart("plugin:" + i, "restart " + i);
        using (var bounded = JsonDocument.Parse(JsonSerializer.Serialize(runtime.CreateRestartNotice())))
            if (bounded.RootElement.GetProperty("reasons").GetArrayLength() != 20) throw new Exception("Restart reasons must be bounded.");
        var restarted = new HostRuntimeState(DateTimeOffset.UtcNow);
        using (var fresh = JsonDocument.Parse(JsonSerializer.Serialize(restarted.CreateRestartNotice())))
            if (fresh.RootElement.GetProperty("recommended").GetBoolean()) throw new Exception("A new process must clear previous restart recommendations.");
        Console.WriteLine("PASS: DashboardRestartNoticePersistsDeduplicatesBoundsReasonsAndResetsOnRestart");
    }
}
