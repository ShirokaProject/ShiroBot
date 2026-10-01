using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Hosting.Runtime;

/// <summary>
/// Restart and shutdown for the console and the Dashboard. Both end with the normal graceful
/// shutdown (plugins unloaded, adapters stopped) instead of killing the process.
/// </summary>
internal sealed class HostPowerControl(Action requestShutdown)
{
    /// <summary>Set on a replacement process: wait for this PID to exit before starting.</summary>
    internal const string WaitForPidVariable = "SHIROBOT_RESTART_WAIT_PID";

    /// <summary>EX_TEMPFAIL: lets a supervisor (systemd Restart=on-failure, Docker) start the host again.</summary>
    internal const int SupervisedRestartExitCode = 75;

    private int _requested;
    private string? _restartExecutable;
    private string[]? _restartArguments;
    private readonly byte[]? _terminalState = CaptureTerminalState();

    public int ExitCode { get; private set; }

    internal bool IsRequested => Volatile.Read(ref _requested) != 0;

    public HostPowerResult Shutdown()
    {
        if (!TryBegin()) return HostPowerResult.Busy;
        BotLog.Warning("收到关机请求，正在安全退出...");
        ScheduleShutdown();
        // A supervisor configured to always restart brings the process back; only it can stop the host for good.
        return new HostPowerResult(true, HostEnvironmentInfo.RunMode switch
        {
            "docker" => "ShiroBot 正在关闭。若容器配置了 restart: always 或 unless-stopped 会被重新启动，彻底停止请使用 docker compose stop。",
            "systemd" => "ShiroBot 正在关闭。若服务配置了 Restart=always 会被重新启动，彻底停止请使用 systemctl stop。",
            _ => "ShiroBot 正在关闭。"
        });
    }

    public HostPowerResult Restart()
    {
        if (!TryBegin()) return HostPowerResult.Busy;

        var mode = HostEnvironmentInfo.RunMode;
        if (mode is "docker" or "systemd")
        {
            // A process supervised by Docker or systemd must not spawn its own successor: the
            // supervisor would kill it with the container/unit. Exit and let the supervisor restart.
            ExitCode = SupervisedRestartExitCode;
            BotLog.Warning($"收到重启请求，进程将退出并由 {(mode == "docker" ? "Docker" : "systemd")} 重新启动...");
            ScheduleShutdown();
            return new HostPowerResult(true, mode == "docker"
                ? "ShiroBot 正在重启。容器需要配置重启策略（如 restart: unless-stopped）才会自动重新启动。"
                : "ShiroBot 正在重启。服务需要配置 Restart=on-failure 或 always 才会自动重新启动。");
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            Interlocked.Exchange(ref _requested, 0);
            return new HostPowerResult(false, "无法确定当前程序路径，重启失败。");
        }

        try
        {
            var arguments = GetRestartArguments(processPath);
            if (!OperatingSystem.IsWindows())
            {
                // exec preserves the PID and foreground process group. Spawning a child and
                // exiting would return the terminal to the shell, leaving the child unable to read keys.
                _restartExecutable = processPath;
                _restartArguments = arguments;
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = processPath,
                    WorkingDirectory = Environment.CurrentDirectory,
                    UseShellExecute = false
                };
                foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
                startInfo.Environment[WaitForPidVariable] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Process.Start(startInfo);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _requested, 0);
            return new HostPowerResult(false, "重启失败: " + ex.Message);
        }

        BotLog.Warning("收到重启请求，正在安全退出，清理完成后重新启动...");
        ScheduleShutdown();
        return new HostPowerResult(true, "ShiroBot 正在重启，稍后重新连接即可。");
    }

    /// <summary>Called first thing on startup: a restarted process waits for its predecessor to exit.</summary>
    internal static async Task WaitForPredecessorAsync()
    {
        var value = Environment.GetEnvironmentVariable(WaitForPidVariable);
        Environment.SetEnvironmentVariable(WaitForPidVariable, null);
        if (!int.TryParse(value, out var pid) || pid == Environment.ProcessId) return;

        try
        {
            using var predecessor = Process.GetProcessById(pid);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await predecessor.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (OperationCanceledException)
        {
            // Start anyway; a port still in use is then reported as a normal startup error.
        }
    }

    internal static string[] GetRestartArguments(string processPath)
    {
        var arguments = Environment.GetCommandLineArgs();
        // dotnet needs the entry DLL too; an apphost/single-file executable does not.
        return string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? arguments
            : arguments.Skip(1).ToArray();
    }

    /// <summary>Run only after HTTP, adapters and rendering have stopped.</summary>
    internal void CompleteRestart()
    {
        if (_restartExecutable is not { } executable || _restartArguments is not { } arguments) return;
        var strings = new[] { executable }.Concat(arguments).Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var argv = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        try
        {
            for (var i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]);
            Marshal.WriteIntPtr(argv, strings.Length * IntPtr.Size, IntPtr.Zero);
            System.Console.Out.Flush();
            System.Console.Error.Flush();
            // exec does not run the CLR's terminal cleanup. Restore the original terminal
            // settings so the successor (and later the shell) starts with a normal terminal.
            if (_terminalState is not null) TcSetAttr(0, 0, _terminalState);
            ExecV(executable, argv);
            var error = Marshal.GetLastPInvokeError();
            Environment.ExitCode = 1;
            BotLog.Error("重新启动主程序失败，请手动启动: " + new Win32Exception(error).Message);
        }
        finally
        {
            Marshal.FreeHGlobal(argv);
            foreach (var pointer in strings) Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static byte[]? CaptureTerminalState()
    {
        if (OperatingSystem.IsWindows()) return null;
        var state = new byte[256]; // large enough for macOS and Linux struct termios
        return TcGetAttr(0, state) == 0 ? state : null;
    }

    [DllImport("libc", EntryPoint = "execv", SetLastError = true)]
    private static extern int ExecV([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr argv);

    [DllImport("libc", EntryPoint = "tcgetattr")]
    private static extern int TcGetAttr(int fd, [Out] byte[] state);

    [DllImport("libc", EntryPoint = "tcsetattr")]
    private static extern int TcSetAttr(int fd, int action, byte[] state);

    private bool TryBegin() => Interlocked.Exchange(ref _requested, 1) == 0;

    // Leave time for the HTTP response to reach the Dashboard before the server stops.
    private void ScheduleShutdown() => _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        requestShutdown();
    });
}

internal sealed record HostPowerResult(bool Ok, string Message)
{
    public static HostPowerResult Busy { get; } = new(false, "已有重启或关机请求正在执行。");
}
