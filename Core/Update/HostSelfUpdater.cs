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
/// Checks for and installs a new host release. The running executable is renamed aside, the new one
/// takes its path, and the host restarts through
/// <see cref="HostPowerControl"/>, which knows whether to spawn a successor or leave it to systemd.
/// </summary>
internal static class HostSelfUpdater
{
    private const string PreviousSuffix = ".old";
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

    /// <summary>Why this host cannot replace itself, or null when it can.</summary>
    public static string? BlockedReason() => HostEnvironmentInfo.RunMode == "docker"
        ? "Docker 中运行的主程序请通过更新镜像升级：docker compose pull && docker compose up -d。"
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
        var directory = Path.GetDirectoryName(processPath)!;
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

            cancellationToken.ThrowIfCancellationRequested();
            if (powerControl.IsRequested) return HostPowerResult.Busy;
            ReplaceExecutable(replacement, processPath);
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
