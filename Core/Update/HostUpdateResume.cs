using System.Text.Json;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Update;

/// <summary>
///     Carries an automatic run across the next restart: a host update, or plugin and adapter updates that could only
///     be staged. Staged components are recorded whether the run restarts the host itself or leaves that to the owner,
///     and accumulate until a restart; the restarted host takes the record once its plugins are loaded and reports
///     whether the host and the staged components really came up on their new versions. After a
///     host update it also finishes the plugin and adapter updates there, so they load into the host they were built
///     for. A missing or unreadable file only means nothing is resumed.
/// </summary>
internal sealed class HostUpdateResume(string path)
{
    public void Save(HostUpdateResumeState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BotLog.Warning("无法记录主程序更新状态，重启后不会发送更新结果: " + ex.Message);
        }
    }

    /// <summary>Adds staged components, keeping the version each was first staged from.</summary>
    public void AddStaged(IReadOnlyList<StagedComponentUpdate> staged)
    {
        var state = Read();
        var merged = (state?.Staged ?? []).ToList();
        foreach (var item in staged)
        {
            var index = merged.FindIndex(existing => existing.Kind == item.Kind &&
                                                     string.Equals(existing.Id, item.Id,
                                                         StringComparison.OrdinalIgnoreCase));
            if (index < 0) merged.Add(item);
            else merged[index] = item with { From = merged[index].From };
        }

        Save((state ?? new HostUpdateResumeState(null, null, DateTimeOffset.UtcNow)) with { Staged = merged });
    }

    /// <summary>Records a host update about to restart the host, keeping the components already staged.</summary>
    public void BeginHostUpdate(string from, string to)
    {
        Save(new HostUpdateResumeState(from, to, DateTimeOffset.UtcNow, Read()?.Staged));
    }

    /// <summary>Forgets a host update that did not happen; staged components still wait for the next restart.</summary>
    public void CancelHostUpdate()
    {
        if (Read()?.Staged is { Count: > 0 } staged)
            Save(new HostUpdateResumeState(null, null, DateTimeOffset.UtcNow, staged));
        else
            Clear();
    }

    private HostUpdateResumeState? Read()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<HostUpdateResumeState>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            BotLog.Warning("读取自动更新状态失败，将重新记录: " + ex.Message);
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BotLog.Warning("清除主程序更新状态失败: " + ex.Message);
        }
    }

    /// <summary>Returns the saved state and removes it, so a crash while resuming does not repeat it forever.</summary>
    public HostUpdateResumeState? Take()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<HostUpdateResumeState>(File.ReadAllText(path));
            File.Delete(path);
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            BotLog.Warning("读取主程序更新状态失败，本次不发送更新结果: " + ex.Message);
            Clear();
            return null;
        }
    }
}

/// <param name="From">Host version before the update, or null when the restart is only for staged components.</param>
/// <param name="To">Host version being installed, or null when the restart is only for staged components.</param>
/// <param name="StartedAt">When the restart was started.</param>
/// <param name="Staged">Plugins and adapters staged for the restart.</param>
internal sealed record HostUpdateResumeState(
    string? From,
    string? To,
    DateTimeOffset StartedAt,
    IReadOnlyList<StagedComponentUpdate>? Staged = null);

/// <summary>A plugin or adapter whose new version is applied by the next start.</summary>
/// <param name="Kind"><c>plugin</c> or <c>adapter</c>.</param>
internal sealed record StagedComponentUpdate(string Kind, string Id, string From, string To)
{
    public const string Plugin = "plugin";
    public const string Adapter = "adapter";

    public string Label => (Kind == Adapter ? "适配器 " : "插件 ") + Id;
}
