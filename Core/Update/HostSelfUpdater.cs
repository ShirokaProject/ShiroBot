using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using ShiroBot.Hosting.Runtime;

namespace ShiroBot.Update;

/// <summary>Result of checking the host's release repository.</summary>
internal sealed record HostUpdateCheck(
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string AssetName,
    string? AssetDownloadUrl,
    string? ReleaseUrl,
    string? ReleaseNotes,
    bool CanApply,
    string? Reason);

/// <summary>
/// Checks for and installs a new host release. The new executable must pass <c>--self-check</c> here
/// first; then the running executable is renamed aside, the new one takes its path, and the host
/// restarts through <see cref="HostPowerControl"/>, which knows whether to spawn a successor or leave
/// it to systemd. In Docker the new executable goes to <see cref="UpdateDirectoryVariable"/> on the
/// data volume instead, and the entrypoint starts it when it is newer than the image's.
/// </summary>
internal static class HostSelfUpdater
{
    private const string PreviousSuffix = ".old";
    internal const string SelfCheckArgument = "--self-check";
    internal const string UpdateDirectoryVariable = "SHIROBOT_HOST_UPDATE_DIR";
    private static readonly TimeSpan SelfCheckTimeout = TimeSpan.FromMinutes(1);
    private static readonly SemaphoreSlim ApplyGate = new(1, 1);

    public static string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The release asset matching this build, e.g. shirobot-host-linux-x64-self-contained.zip.</summary>
    public static string CurrentAssetName
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly();
            var runtime = GetMetadata(assembly, "ShiroBot.RuntimeIdentifier");
            if (string.IsNullOrWhiteSpace(runtime)) runtime = RuntimeInformation.RuntimeIdentifier;
            var publishKind = string.Equals(GetMetadata(assembly, "ShiroBot.SelfContained"), "true", StringComparison.OrdinalIgnoreCase)
                ? "self-contained"
                : "framework-dependent";
            return $"shirobot-host-{runtime}-{publishKind}.zip";
        }
    }

    public static async Task<HostUpdateCheck> CheckAsync(string repository, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return new HostUpdateCheck(CurrentVersion, null, false, CurrentAssetName, null, null, null, false, "未配置主程序更新仓库 host_update_repository。");

        var update = await Updater.CheckGitHubReleaseAssetAsync(repository, CurrentVersion, CurrentAssetName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (update is null)
            return new HostUpdateCheck(CurrentVersion, CurrentVersion, false, CurrentAssetName, null, null, null, false, null);

        var reason = BlockedReason() ??
                     (string.IsNullOrWhiteSpace(update.AssetDownloadUrl) ? $"新版本没有当前运行形态对应的安装包 {CurrentAssetName}。" : null);
        return new HostUpdateCheck(
            update.CurrentVersion,
            update.LatestVersion,
            true,
            CurrentAssetName,
            update.AssetDownloadUrl,
            update.ReleaseUrl,
            update.ReleaseNotes,
            reason is null,
            reason);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Assembly.Location being empty is the intended runtime check for a bundled single-file host.")]
    private static bool IsSingleFile => string.IsNullOrEmpty(typeof(HostSelfUpdater).Assembly.Location);

    private static string? UpdateDirectory =>
        Environment.GetEnvironmentVariable(UpdateDirectoryVariable) is { Length: > 0 } directory ? directory : null;

    /// <summary>Why this host cannot replace itself, or null when it can.</summary>
    public static string? BlockedReason() => HostEnvironmentInfo.RunMode == "docker" && UpdateDirectory is null
        ? "当前镜像不支持在面板中更新主程序，请更新镜像：docker compose pull && docker compose up -d。"
        : !IsSingleFile
            ? "当前不是单文件发布版本，请使用 Release 发布包后再进行自更新。"
        : string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? "无法确定当前主程序的文件路径。"
            : null;

    /// <summary>Downloads and installs <paramref name="assetDownloadUrl"/>, then restarts the host.</summary>
    public static async Task<HostPowerResult> ApplyAsync(
        string assetDownloadUrl,
        HostPowerControl powerControl,
        CancellationToken cancellationToken = default)
    {
        if (BlockedReason() is { } blocked) return new HostPowerResult(false, blocked);
        if (!await ApplyGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new HostPowerResult(false, "已有宿主更新正在执行。");
        try
        {
            if (powerControl.IsRequested) return HostPowerResult.Busy;
            return await ApplyCoreAsync(assetDownloadUrl, powerControl, cancellationToken).ConfigureAwait(false);
        }
        finally { ApplyGate.Release(); }
    }

    private static async Task<HostPowerResult> ApplyCoreAsync(string assetDownloadUrl, HostPowerControl powerControl, CancellationToken cancellationToken)
    {
        var processPath = Path.GetFullPath(Environment.ProcessPath!);
        // The image's /app is discarded with the container, so Docker installs onto the data volume.
        var targetPath = UpdateDirectory is { } updateDirectory
            ? Path.Combine(Path.GetFullPath(updateDirectory), Path.GetFileName(processPath))
            : processPath;
        var directory = Path.GetDirectoryName(targetPath)!;
        // Same volume as the executable, so the final move is a rename.
        var workRoot = Path.Combine(directory, ".tmp", "ShiroBot.Update", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workRoot);
            var packagePath = Path.Combine(workRoot, "package.zip");
            await Updater.DownloadFileAsync(assetDownloadUrl, packagePath, cancellationToken).ConfigureAwait(false);
            var extractRoot = Path.Combine(workRoot, "extract");
            ZipFile.ExtractToDirectory(packagePath, extractRoot, overwriteFiles: true);
            var replacement = Directory.EnumerateFiles(extractRoot, Path.GetFileName(processPath), SearchOption.AllDirectories)
                .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"更新包中没有主程序文件 {Path.GetFileName(processPath)}。");

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(replacement, File.GetUnixFileMode(replacement) | UnixFileMode.UserRead | UnixFileMode.UserExecute);
            // Only a version that starts on this runtime may replace the working one.
            if (await SelfCheckAsync(replacement, cancellationToken).ConfigureAwait(false) is { } failure)
            {
                return new HostPowerResult(false, HostEnvironmentInfo.RunMode == "docker"
                    ? "新版本无法在当前镜像中运行，可能需要新的 .NET 运行时或系统依赖，请更新镜像：docker compose pull && docker compose up -d。（" + failure + "）"
                    : "新版本无法在当前环境中运行，可能需要安装新的 .NET 运行时。（" + failure + "）");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (powerControl.IsRequested) return HostPowerResult.Busy;
            InstallExecutable(replacement, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or InvalidDataException)
        {
            return new HostPowerResult(false, "主程序更新失败: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(workRoot, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            StartupTempCleanup.DeleteEmpty(Path.GetDirectoryName(workRoot)!);
            StartupTempCleanup.DeleteEmpty(Path.Combine(directory, ".tmp"));
        }

        var restart = powerControl.Restart();
        return restart.Ok
            ? new HostPowerResult(true, "新版本已安装，" + restart.Message)
            : new HostPowerResult(false, "新版本已安装，但自动重启失败，请手动重启主程序: " + restart.Message);
    }

    /// <summary>Runs <paramref name="executable"/> with <c>--self-check</c>; returns why it failed, or null.</summary>
    internal static async Task<string?> SelfCheckAsync(string executable, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(SelfCheckArgument);
        startInfo.Environment.Remove(HostPowerControl.WaitForPidVariable);
        using var process = new Process { StartInfo = startInfo };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return "无法启动: " + ex.Message;
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SelfCheckTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            return "自检超时";
        }

        var version = (await output.ConfigureAwait(false)).Trim();
        if (process.ExitCode == 0 && version.Length > 0) return null;
        var detail = (await error.ConfigureAwait(false)).Trim();
        if (detail.Length == 0) detail = version;
        if (detail.Length > 300) detail = detail[..300] + "...";
        return $"退出码 {process.ExitCode}" + (detail.Length > 0 ? ": " + detail : "");
    }

    internal static void InstallExecutable(string replacement, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            ReplaceExecutable(replacement, targetPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(replacement, targetPath);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(targetPath, File.GetUnixFileMode(targetPath) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    internal static void ReplaceExecutable(string replacement, string processPath)
    {
        var previousPath = processPath + PreviousSuffix;
        if (File.Exists(previousPath)) File.Delete(previousPath);
        File.Move(processPath, previousPath);
        try
        {
            File.Move(replacement, processPath);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(processPath, File.GetUnixFileMode(previousPath) | UnixFileMode.UserExecute);
        }
        catch
        {
            File.Move(previousPath, processPath, overwrite: true);
            throw;
        }
    }

    /// <summary>Removes the previous executable left by an update; call once the old process has exited.</summary>
    public static void CleanupPrevious()
    {
        if (!IsSingleFile) return;
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return;
        try { File.Delete(processPath + PreviousSuffix); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string? GetMetadata(Assembly? assembly, string key) =>
        assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
}
