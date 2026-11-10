using System.Globalization;
using System.Text.Json;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Update;

/// <summary>Which updates an automatic run may apply on its own.</summary>
internal static class AutoUpdatePolicy
{
    public static bool IsExcluded(IEnumerable<string> excluded, string id)
    {
        return excluded.Any(value => string.Equals(value.Trim(), id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     True when the major version goes up (1.x → 2.0). Versions without a readable major number, such as dates or
    ///     commit hashes, are not treated as major upgrades, so they still update.
    /// </summary>
    public static bool IsMajorUpgrade(string current, string latest)
    {
        return TryGetMajor(current, out var from) && TryGetMajor(latest, out var to) && to > from;
    }

    internal static bool TryGetMajor(string version, out int major)
    {
        major = 0;
        var text = version.Trim().TrimStart('v', 'V');
        var dot = text.IndexOf('.');
        return dot > 0 && int.TryParse(text.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out major);
    }
}

/// <summary>
///     Remembers which skipped major versions the owners were already told about, so a daily run reports each one once.
///     Stored next to the schedule's last-run file; a missing or unreadable file only means the notice is sent again.
/// </summary>
internal sealed class SkippedMajorNotices(string path)
{
    private Dictionary<string, string>? _notified;

    /// <summary>Returns true the first time <paramref name="version" /> is skipped for <paramref name="key" />.</summary>
    public bool ShouldNotify(string key, string version)
    {
        var notified = _notified ??= Load();
        if (notified.TryGetValue(key, out var previous) &&
            string.Equals(previous, version, StringComparison.OrdinalIgnoreCase))
            return false;
        notified[key] = version;
        Save(notified);
        return true;
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) is { } stored
                    ? new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            BotLog.Warning("读取跳过的大版本记录失败，将重新通知: " + ex.Message);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save(Dictionary<string, string> notified)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(notified));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BotLog.Warning("保存跳过的大版本记录失败: " + ex.Message);
        }
    }
}