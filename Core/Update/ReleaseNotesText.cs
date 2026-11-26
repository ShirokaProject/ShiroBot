using System.Text;
using System.Text.RegularExpressions;

namespace ShiroBot.Update;

/// <summary>
///     Turns GitHub release notes (Markdown) into plain text for chat messages: headings become 【title】, list items
///     become “· ”, inline markup is dropped, and long notes are cut at a line boundary with a pointer to the release.
/// </summary>
internal static partial class ReleaseNotesText
{
    /// <summary>Plain-text notes, or null when there are none.</summary>
    /// <param name="markdown">Release body.</param>
    /// <param name="maxChars">Longest result before it is cut.</param>
    /// <param name="releaseUrl">Shown after a cut so the full notes stay reachable.</param>
    public static string? ToPlain(string? markdown, int maxChars, string? releaseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var text = HtmlComment().Replace(markdown.Replace("\r\n", "\n"), "");
        var lines = new List<string>();
        var inFence = false;
        foreach (var raw in text.Split('\n'))
        {
            // Only what changed: the download section (assets, Docker, NuGet) ends the notes, and the leading
            // version / 更新日志 headings repeat what the report already says.
            if (!inFence && DownloadHeading().IsMatch(raw.Trim())) break;
            if (!inFence && RedundantHeading().IsMatch(raw.Trim())) continue;
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;
            var line = Convert(raw);
            if (line is null) continue;
            // Collapse runs of blank lines and drop leading ones
            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0)) continue;
            lines.Add(line);
        }

        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return null;

        var result = new StringBuilder();
        foreach (var line in lines)
        {
            if (result.Length + line.Length + 1 > maxChars)
            {
                if (result.Length == 0) result.Append(line.AsSpan(0, Math.Max(0, maxChars - 1))).Append('…');
                result.Append(result[^1] == '\n' ? "" : "\n")
                    .Append(string.IsNullOrWhiteSpace(releaseUrl) ? "……" : "……完整日志: " + releaseUrl);
                return result.ToString().TrimEnd();
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>One Markdown line as plain text; null for lines that carry nothing (rules, image-only lines).</summary>
    private static string? Convert(string raw)
    {
        var line = raw.TrimEnd();
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0) return "";
        if (HorizontalRule().IsMatch(trimmed)) return null;

        line = Image().Replace(line, "");
        line = Link().Replace(line, "$1");
        line = Emphasis().Replace(line, "$2");
        line = line.Replace("`", "");
        trimmed = line.TrimStart();
        if (trimmed.Length == 0) return null;

        if (Heading().Match(trimmed) is { Success: true } heading) return $"【{heading.Groups[1].Value.Trim()}】";

        var indent = (line.Length - trimmed.Length) / 2;
        var prefix = new string(' ', Math.Min(indent, 3) * 2);
        if (Bullet().Match(trimmed) is { Success: true } bullet) return prefix + "· " + bullet.Groups[1].Value;
        if (trimmed.StartsWith('>')) return prefix + trimmed.TrimStart('>', ' ');
        return prefix + trimmed;
    }

    [GeneratedRegex(@"^#{1,6}\s*(下载|download)", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadHeading();

    [GeneratedRegex(@"^#{1,6}\s*(v?\d+(\.\d+)+|更新日志|changelog|release notes)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RedundantHeading();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"^(-{3,}|\*{3,}|_{3,})$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(\*\*|__)(.+?)\1")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[-*+]\s+(.*)$")]
    private static partial Regex Bullet();
}
