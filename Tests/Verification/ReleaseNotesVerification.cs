using System.Text.Json;
using ShiroBot.Update;

namespace ShiroBot.Verification;

internal static class ReleaseNotesVerification
{
    internal static void Run()
    {
        const string markdown = """
            <!-- release template -->
            ## 主要更新

            - 新增 **自动更新** 推送日志，详见 [文档](https://docs.example/update)。
              - 子项 `code`
            ![截图](https://img.example/a.png)

            ---
            ```bash
            echo hidden
            ```
            > 引用说明
            """;
        var plain = ReleaseNotesText.ToPlain(markdown, 1000);
        const string expected = "【主要更新】\n\n· 新增 自动更新 推送日志，详见 文档。\n  · 子项 code\n\n引用说明";
        if (plain != expected) throw new Exception("Release notes are not converted to plain text: " + plain);

        const string release = "## v1.0.5\n\n### 更新日志\n\n### 主要更新\n\n- 新功能\n\n## 下载地址\n\n- Windows: [x64](https://x)\n### NuGet 包\n- SDK";
        if (ReleaseNotesText.ToPlain(release, 1000) != "【主要更新】\n\n· 新功能")
            throw new Exception("Download section and repeated headings must be dropped: " + ReleaseNotesText.ToPlain(release, 1000));

        if (ReleaseNotesText.ToPlain("  \n\n", 100) is not null || ReleaseNotesText.ToPlain(null, 100) is not null)
            throw new Exception("Empty release notes must yield null.");

        var longNotes = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"- 第 {i} 项更新内容"));
        var cut = ReleaseNotesText.ToPlain(longNotes, 60, "https://github.com/o/r/releases/tag/v1");
        if (cut is null || cut.Length > 60 + 60 || !cut.EndsWith("……完整日志: https://github.com/o/r/releases/tag/v1") ||
            !cut.StartsWith("· 第 1 项更新内容\n"))
            throw new Exception("Long release notes must be cut at a line with a link: " + cut);

        var oneLong = ReleaseNotesText.ToPlain(new string('长', 200), 20);
        if (oneLong is null || !oneLong.StartsWith(new string('长', 19) + "…") || !oneLong.EndsWith("……"))
            throw new Exception("A single overlong line must still be cut: " + oneLong);

        var root = Path.Combine(Path.GetTempPath(), "shirobot-notes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var resume = new HostUpdateResume(Path.Combine(root, "resume.json"));
            resume.AddStaged([new StagedComponentUpdate(StagedComponentUpdate.Plugin, "Echo", "1.0.0", "1.1.0")]);
            resume.BeginHostUpdate("1.0.4", "1.0.5", "## 1.0.5", "https://host");
            if (resume.Take() is not { ReleaseNotes: "## 1.0.5", ReleaseUrl: "https://host", Staged: [{ Id: "Echo" }] })
                throw new Exception("Host release notes must survive the restart alongside staged components.");

            // A state written by an older host has no notes and still loads
            File.WriteAllText(Path.Combine(root, "resume.json"),
                JsonSerializer.Serialize(new { From = "1.0.3", To = "1.0.4", StartedAt = DateTimeOffset.UtcNow }));
            if (resume.Take() is not { To: "1.0.4", ReleaseNotes: null })
                throw new Exception("Resume state without notes must still load.");
        }
        finally
        {
            Directory.Delete(root, true);
        }

        Console.WriteLine("Release notes verification passed.");
    }
}
