using System.Text.Json;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Update;

/// <summary>
///     Carries an automatic run across the restart a host update causes. The run writes it just before applying the
///     update; the restarted host takes it once its plugins are loaded, reports whether it really came up on the new
///     version and finishes the plugin and adapter updates there, so they load into the host they were built for.
///     A missing or unreadable file only means nothing is resumed.
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

internal sealed record HostUpdateResumeState(string From, string To, DateTimeOffset StartedAt);
