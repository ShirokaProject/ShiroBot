using System.Globalization;
using ShiroBot.Configuration;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Update;

/// <summary>When the daily automatic update is due.</summary>
internal static class AutoUpdateSchedule
{
    public const string DefaultTime = "02:00";

    /// <summary>A host that was not running in this window after the scheduled time skips that day.</summary>
    public static readonly TimeSpan RunWindow = TimeSpan.FromHours(1);

    public static bool TryParseTime(string? value, out TimeOnly time)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) ||
            !TimeOnly.TryParseExact(trimmed, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out time))
        {
            time = default;
            return false;
        }

        // .NET 11 normalizes "24:00" to 00:00; the configuration documents the 00:00–23:59 range.
        if (trimmed.StartsWith("24:", StringComparison.Ordinal))
        {
            time = default;
            return false;
        }

        return true;
    }

    public static bool IsDue(DateTimeOffset localNow, TimeOnly time, DateOnly? lastRun)
    {
        var today = DateOnly.FromDateTime(localNow.DateTime);
        if (lastRun == today) return false;
        var elapsed = localNow.TimeOfDay - time.ToTimeSpan();
        return elapsed >= TimeSpan.Zero && elapsed < RunWindow;
    }
}

/// <summary>
///     Runs <see cref="AutoUpdateRunner" /> once a day at <see cref="UpdateConfig.Time" />, reading the live
///     core config so Dashboard changes apply without a restart. The run date is saved before updating, so the
///     restart an update causes does not run it again the same day.
/// </summary>
internal sealed class AutoUpdateScheduler : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private readonly CoreConfig _config;
    private readonly Func<CancellationToken, Task> _run;
    private readonly string _statePath;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private string? _invalidTimeWarned;
    private int _running;

    public AutoUpdateScheduler(CoreConfig config, Func<CancellationToken, Task> run, string statePath,
        TimeProvider? timeProvider = null)
    {
        _config = config;
        _run = run;
        _statePath = statePath;
        _time = timeProvider ?? TimeProvider.System;
        _timer = _time.CreateTimer(state => _ = TickAsync(), null, CheckInterval, CheckInterval);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _timer.Dispose();
    }

    internal async Task TickAsync()
    {
        if (!_config.Update.Any || _stopping.IsCancellationRequested) return;
        if (!AutoUpdateSchedule.TryParseTime(_config.Update.Time, out var time))
        {
            if (_invalidTimeWarned != _config.Update.Time)
            {
                _invalidTimeWarned = _config.Update.Time;
                BotLog.Warning($"自动更新时间 {_config.Update.Time} 不是 HH:mm 格式，改用默认的 {AutoUpdateSchedule.DefaultTime}。");
            }

            time = TimeOnly.ParseExact(AutoUpdateSchedule.DefaultTime, "HH:mm", CultureInfo.InvariantCulture);
        }

        var now = _time.GetLocalNow();
        if (!AutoUpdateSchedule.IsDue(now, time, ReadLastRun())) return;
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        try
        {
            WriteLastRun(DateOnly.FromDateTime(now.DateTime));
            BotLog.Info("开始执行每日自动更新...");
            await _run(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            BotLog.Error("自动更新失败: " + ex.Message);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private DateOnly? ReadLastRun()
    {
        try
        {
            return File.Exists(_statePath) &&
                   DateOnly.TryParseExact(File.ReadAllText(_statePath).Trim(), "yyyy-MM-dd",
                       CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteLastRun(DateOnly date)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BotLog.Warning("无法记录自动更新时间，重启后当天可能再次检查更新: " + ex.Message);
        }
    }
}