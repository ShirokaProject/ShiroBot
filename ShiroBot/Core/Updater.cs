using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Core;

public sealed record GitHubPluginPackage(
    string Repository,
    string Version,
    string ReleaseName,
    string ReleaseUrl,
    string Body,
    string? AssetDownloadUrl,
    string? AssetName,
    string? AssetType);

public static class Updater
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, PendingUpdateEntry> PendingUpdates = new(StringComparer.OrdinalIgnoreCase);
    private static Func<IReadOnlyList<long>> _getOwnerIds = () => [];
    private static Func<long, string, Task> _sendPrivateMessageAsync = (_, _) => Task.CompletedTask;
    private static string? _githubProxy;

    public static void Initialize(
        Func<IReadOnlyList<long>> getOwnerIds,
        Func<long, string, Task> sendPrivateMessageAsync,
        string? githubProxy = null)
    {
        _getOwnerIds = getOwnerIds;
        _sendPrivateMessageAsync = sendPrivateMessageAsync;
        _githubProxy = githubProxy;
    }

    public static async Task<GitHubReleaseUpdate?> CheckGitHubReleaseAsync(
        string repository,
        string currentVersion,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository cannot be empty.", nameof(repository));
        }

        var release = await GetLatestReleaseAsync(repository, includePrerelease, cancellationToken);
        if (release is null)
        {
            return null;
        }

        if (!IsNewerVersion(release.TagName, currentVersion))
        {
            return null;
        }

        return new GitHubReleaseUpdate(
            repository,
            NormalizeVersion(currentVersion),
            NormalizeVersion(release.TagName),
            release.Name,
            release.HtmlUrl,
            release.Body,
            release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))?.DownloadUrl,
            release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))?.Name);
    }

    public static async Task<GitHubReleaseUpdate?> CheckGitHubReleaseAssetAsync(
        string repository,
        string currentVersion,
        string assetName,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository cannot be empty.", nameof(repository));
        }

        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new ArgumentException("Asset name cannot be empty.", nameof(assetName));
        }

        var release = await GetLatestReleaseAsync(repository, includePrerelease, cancellationToken);
        if (release is null || !IsNewerVersion(release.TagName, currentVersion))
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(item => string.Equals(item.Name, assetName, StringComparison.OrdinalIgnoreCase));
        return new GitHubReleaseUpdate(
            repository,
            NormalizeVersion(currentVersion),
            NormalizeVersion(release.TagName),
            release.Name,
            release.HtmlUrl,
            release.Body,
            asset?.DownloadUrl,
            asset?.Name);
    }

    public static async Task<GitHubPluginPackage?> GetLatestPluginPackageAsync(
        string repository,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("Repository cannot be empty.", nameof(repository));
        }

        var release = await GetLatestReleaseAsync(repository, includePrerelease, cancellationToken);
        if (release is null)
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ?? release.Assets.FirstOrDefault(item => item.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return new GitHubPluginPackage(
                repository,
                NormalizeVersion(release.TagName),
                release.Name,
                release.HtmlUrl,
                release.Body,
                null,
                null,
                null);
        }

        return new GitHubPluginPackage(
            repository,
            NormalizeVersion(release.TagName),
            release.Name,
            release.HtmlUrl,
            release.Body,
            asset.DownloadUrl,
            asset.Name,
            Path.GetExtension(asset.Name).TrimStart('.').ToLowerInvariant());
    }

    public static async Task<string> RequestHostUpdateAsync(
        GitHubReleaseUpdate update,
        CancellationToken cancellationToken = default)
    {
        var id = CreatePendingUpdate(
            UpdateTarget.Host,
            "宿主",
            update.CurrentVersion,
            update.LatestVersion,
            update.ReleaseUrl,
            async () => await UpdateSelfAsync(update.AssetDownloadUrl, cancellationToken));

        await NotifyOwnersAsync(
            $"检测到宿主更新: {update.CurrentVersion} -> {update.LatestVersion}\n" +
            $"来源: {update.Repository}\n" +
            (string.IsNullOrWhiteSpace(update.AssetName) ? string.Empty : $"文件: {update.AssetName}\n") +
            $"确认更新: update confirm {id}\n" +
            $"取消更新: update cancel {id}",
            cancellationToken);

        return id;
    }

    public static async Task<string> RequestPluginUpdateAsync(
        PluginUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var id = CreatePendingUpdate(
            UpdateTarget.Plugin,
            request.PluginName,
            request.CurrentVersion,
            request.LatestVersion,
            request.ReleaseUrl,
            async () => await UpdatePluginAsync(request.PluginName, request.AssetDownloadUrl, request.TargetPath, cancellationToken));

        await NotifyOwnersAsync(
            $"检测到插件更新: {request.PluginName} {request.CurrentVersion} -> {request.LatestVersion}\n" +
            (string.IsNullOrWhiteSpace(request.ReleaseUrl) ? string.Empty : $"来源: {request.ReleaseUrl}\n") +
            (string.IsNullOrWhiteSpace(request.AssetDownloadUrl) ? string.Empty : $"文件: {request.AssetDownloadUrl}\n") +
            $"确认更新: update confirm {id}\n" +
            $"取消更新: update cancel {id}",
            cancellationToken);

        return id;
    }

    public static IReadOnlyList<PendingUpdateInfo> GetPendingUpdates()
    {
        return PendingUpdates.Values
            .OrderBy(item => item.CreatedAt)
            .Select(item => new PendingUpdateInfo(
                item.Id,
                item.Target,
                item.Name,
                item.CurrentVersion,
                item.LatestVersion,
                item.ReleaseUrl))
            .ToList();
    }

    public static async Task<bool> ConfirmUpdateAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (!PendingUpdates.TryRemove(requestId, out var entry))
        {
            return false;
        }

        if (entry.Target == UpdateTarget.Host)
        {
            await NotifyOwnersAsync($"宿主更新即将执行并重启: {entry.Name} ({entry.Id})", cancellationToken);
            await entry.ExecuteAsync(cancellationToken);
            return true;
        }

        await entry.ExecuteAsync(cancellationToken);
        await NotifyOwnersAsync($"更新任务已执行: {entry.Name} ({entry.Id})", cancellationToken);
        return true;
    }

    public static bool CancelUpdate(string requestId)
    {
        return PendingUpdates.TryRemove(requestId, out _);
    }

    public static Task UpdateSelfAsync(CancellationToken cancellationToken = default) =>
        UpdateSelfAsync(assetDownloadUrl: null, cancellationToken);

    private static async Task UpdateSelfAsync(string? assetDownloadUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetDownloadUrl))
        {
            throw new InvalidOperationException("宿主更新缺少下载地址。");
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("无法确定当前宿主可执行文件路径。");
        }

        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var tempRoot = Path.Combine(installDirectory, ".tmp", "ShiroBot.Update", Guid.NewGuid().ToString("N"));
        var assetFileName = GetDownloadFileName(assetDownloadUrl);
        var packagePath = Path.Combine(tempRoot, assetFileName);
        var extractRoot = Path.Combine(tempRoot, "extract");

        Directory.CreateDirectory(tempRoot);

        try
        {
            await DownloadFileAsync(assetDownloadUrl, packagePath, cancellationToken);
            if (!IsZipPackage(packagePath))
            {
                throw new InvalidOperationException($"宿主更新包必须是 zip 文件: {assetFileName}");
            }

            var replacementExecutable = FindReplacementExecutableInZipPackage(packagePath, extractRoot, processPath)
                                        ?? throw new InvalidOperationException($"更新包中未找到宿主可执行文件: {Path.GetFileName(processPath)}");
            var restartArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var scriptPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? CreateWindowsSelfUpdateScript(tempRoot, replacementExecutable, processPath, restartArguments)
                : CreateUnixSelfUpdateScript(tempRoot, replacementExecutable, processPath, restartArguments);

            StartSelfUpdateScript(scriptPath);
        }
        catch
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // ignored: best-effort cleanup
            }

            throw;
        }

        Environment.Exit(0);
    }

    public static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        string? expectedSha256 = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApplyGithubProxy(url));
        request.Headers.UserAgent.ParseAdd("ShiroBot-Updater");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (maxBytes is { } limit && response.Content.Headers.ContentLength is { } contentLength && contentLength > limit)
        {
            throw new InvalidOperationException($"下载文件超过大小限制: {contentLength} > {limit}");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            totalBytes += read;
            if (maxBytes is { } maximum && totalBytes > maximum)
            {
                throw new InvalidOperationException($"下载文件超过大小限制: {totalBytes} > {maximum}");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"下载文件 SHA-256 校验失败。Expected {expectedSha256}, actual {actualSha256}。");
            }
        }
    }

    private static string GetDownloadFileName(string downloadUrl)
    {
        var uri = Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsed)
            ? parsed
            : null;

        var fileName = uri is null
            ? Path.GetFileName(downloadUrl)
            : Path.GetFileName(uri.LocalPath);

        return string.IsNullOrWhiteSpace(fileName)
            ? "host-update.bin"
            : fileName;
    }

    private static bool IsZipPackage(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            Span<byte> header = stackalloc byte[4];
            using var stream = File.OpenRead(path);
            return stream.Read(header) == 4 &&
                   header[0] == 0x50 && header[1] == 0x4B &&
                   header[2] == 0x03 && header[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindReplacementExecutableInZipPackage(string packagePath, string extractRoot, string processPath)
    {
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(packagePath, extractRoot, overwriteFiles: true);

        var fileName = Path.GetFileName(processPath);
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        return Directory.EnumerateFiles(extractRoot, fileName, SearchOption.AllDirectories)
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .FirstOrDefault();
    }

    private static string CreateWindowsSelfUpdateScript(
        string tempRoot,
        string sourceExecutable,
        string processPath,
        IReadOnlyList<string> restartArguments)
    {
        var scriptPath = Path.Combine(tempRoot, "apply-update.cmd");
        var currentPid = Environment.ProcessId;
        var arguments = string.Join(" ", restartArguments.Select(WindowsArgumentQuote));
        var executableDirectory = Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;
        var logPath = Path.Combine(executableDirectory, "ShiroBot.update.log");
        var script = $$"""
@echo off
setlocal
set "PID={{currentPid}}"
set "SRC={{sourceExecutable}}"
set "EXE={{processPath}}"
set "EXEDIR={{executableDirectory}}"
set "UPDATE_TMP={{tempRoot}}"
set "RUNTIME_TMP=%EXEDIR%\.tmp\runtime"
set "ARGS={{arguments}}"
set "LOG={{logPath}}"

(
  echo [%date% %time%] waiting process %PID%
) >> "%LOG%" 2>&1

:wait_process
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Get-Process -Id %PID% -ErrorAction Stop ^| Out-Null; exit 0 } catch { exit 1 }" >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    timeout /t 1 /nobreak >nul
    goto wait_process
)

(
  echo [%date% %time%] copying "%SRC%" to "%EXE%"
  copy /Y "%SRC%" "%EXE%"
) >> "%LOG%" 2>&1
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

if not exist "%RUNTIME_TMP%" mkdir "%RUNTIME_TMP%" >nul 2>nul
set "TMP=%RUNTIME_TMP%"
set "TEMP=%RUNTIME_TMP%"

(
  echo [%date% %time%] runtime temp dir "%RUNTIME_TMP%"
  echo [%date% %time%] starting new cmd: "%EXE%" %ARGS%
  start "ShiroBot" /D "%EXEDIR%" cmd.exe /k ""%EXE%" %ARGS%"
  echo [%date% %time%] start command returned %ERRORLEVEL%
) >> "%LOG%" 2>&1

cd /d "%TEMP%"
rmdir /s /q "%UPDATE_TMP%" >nul 2>nul
rmdir "%EXEDIR%\.tmp\ShiroBot.Update" >nul 2>nul
exit /b 0
""";
        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        return scriptPath;
    }

    private static string CreateUnixSelfUpdateScript(
        string tempRoot,
        string sourceExecutable,
        string processPath,
        IReadOnlyList<string> restartArguments)
    {
        var scriptPath = Path.Combine(tempRoot, "apply-update.sh");
        var currentPid = Environment.ProcessId;
        var arguments = string.Join(" ", restartArguments.Select(ShellQuote));
        var executableDirectory = Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;
        var logPath = Path.Combine(executableDirectory, "ShiroBot.update.log");
        var script = $$"""
#!/bin/sh
PID={{currentPid}}
SRC={{ShellQuote(sourceExecutable)}}
EXE={{ShellQuote(processPath)}}
EXEDIR={{ShellQuote(executableDirectory)}}
UPDATE_TMP={{ShellQuote(tempRoot)}}
RUNTIME_TMP="$EXEDIR/.tmp/runtime"
ARGS="{{arguments}}"
LOG={{ShellQuote(logPath)}}

echo "[$(date)] waiting process $PID" >> "$LOG" 2>&1
while kill -0 "$PID" 2>/dev/null; do
  sleep 1
done

echo "[$(date)] copying $SRC to $EXE" >> "$LOG" 2>&1
cp -f "$SRC" "$EXE" >> "$LOG" 2>&1
chmod +x "$EXE" 2>/dev/null || true

mkdir -p "$RUNTIME_TMP"
export TMPDIR="$RUNTIME_TMP"
export TMP="$RUNTIME_TMP"
export TEMP="$RUNTIME_TMP"
echo "[$(date)] runtime temp dir $RUNTIME_TMP" >> "$LOG" 2>&1
echo "[$(date)] starting $EXE $ARGS" >> "$LOG" 2>&1
cd "$EXEDIR" || exit 1
# shellcheck disable=SC2086
nohup "$EXE" $ARGS >/dev/null 2>&1 &
echo "[$(date)] started pid $!" >> "$LOG" 2>&1
rm -rf "$UPDATE_TMP"
rmdir "$EXEDIR/.tmp/ShiroBot.Update" 2>/dev/null || true
""";
        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                File.SetUnixFileMode(scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
                // chmod through /bin/sh is enough on platforms that do not support File.SetUnixFileMode.
            }
        }

        return scriptPath;
    }

    private static void StartSelfUpdateScript(string scriptPath)
    {
        var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"\"" + scriptPath + "\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
            : new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = ShellQuote(scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true
            };

        Process.Start(startInfo);
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string WindowsArgumentQuote(string value)
    {
        if (value.Length == 0) return "\"\"";
        if (!value.Any(char.IsWhiteSpace) && value.IndexOfAny(['\"', '\\']) < 0) return value;

        var builder = new StringBuilder();
        builder.Append('\"');
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '\"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append(c);
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            builder.Append(c);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('\"');
        return builder.ToString();
    }

    public static async Task UpdatePluginAsync(string pluginName, string? assetDownloadUrl, string? targetPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetDownloadUrl))
        {
            throw new InvalidOperationException($"插件 {pluginName} 的更新缺少 DLL 下载地址。");
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"插件 {pluginName} 的更新缺少目标路径。");
        }

        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? AppContext.BaseDirectory;
        var tempDirectory = Path.Combine(targetDirectory, ".tmp");
        Directory.CreateDirectory(tempDirectory);

        var tempPath = Path.Combine(
            tempDirectory,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.download");

        try
        {
            await DownloadFileAsync(assetDownloadUrl, tempPath, cancellationToken);
            File.Copy(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                DeleteDirectoryIfEmpty(tempDirectory);
            }
            catch
            {
                // ignored: best-effort cleanup
            }
        }
    }

    private static void DeleteDirectoryIfEmpty(string directory)
    {
        if (!Directory.Exists(directory)) return;
        if (Directory.EnumerateFileSystemEntries(directory).Any()) return;
        Directory.Delete(directory);
    }

    private static string CreatePendingUpdate(
        UpdateTarget target,
        string name,
        string currentVersion,
        string latestVersion,
        string? releaseUrl,
        Func<Task> action)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        PendingUpdates[id] = new PendingUpdateEntry(
            id,
            target,
            name,
            NormalizeVersion(currentVersion),
            NormalizeVersion(latestVersion),
            releaseUrl,
            action);
        return id;
    }

    private static async Task NotifyOwnersAsync(string content, CancellationToken cancellationToken)
    {
        var ownerIds = _getOwnerIds();
        foreach (var ownerId in ownerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _sendPrivateMessageAsync(ownerId, content);
        }
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync(
        string repository,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        return await GetLatestReleaseFromApiAsync(repository, includePrerelease, cancellationToken);
    }

    private static async Task<GitHubRelease?> GetLatestReleaseFromApiAsync(
        string repository,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            includePrerelease
                ? $"https://api.github.com/repos/{repository}/releases?per_page=20"
                : $"https://api.github.com/repos/{repository}/releases/latest");

        request.Headers.UserAgent.ParseAdd("ShiroBot-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var item = includePrerelease
            ? document.RootElement.EnumerateArray().FirstOrDefault(release =>
                !release.TryGetProperty("draft", out var draft) || !draft.GetBoolean())
            : document.RootElement;
        if (item.ValueKind != JsonValueKind.Object) return null;

        return new GitHubRelease(
            item.GetPropertyOrDefault("tag_name"),
            item.GetPropertyOrDefault("name"),
            item.GetPropertyOrDefault("html_url"),
            item.GetPropertyOrDefault("body"),
            item.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean(),
            item.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
            GetReleaseAssets(item));
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        var normalizedLatest = NormalizeVersion(latestVersion);
        var normalizedCurrent = NormalizeVersion(currentVersion);

        if (Version.TryParse(normalizedLatest, out var latest) && Version.TryParse(normalizedCurrent, out var current))
        {
            return latest > current;
        }

        return !string.Equals(normalizedLatest, normalizedCurrent, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version)
    {
        var trimmed = version.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? trimmed[1..]
            : trimmed;
    }

    private sealed record GitHubRelease(
        string TagName,
        string Name,
        string HtmlUrl,
        string Body,
        bool Prerelease,
        bool Draft,
        IReadOnlyList<GitHubReleaseAsset> Assets);

    private sealed record GitHubReleaseAsset(string Name, string DownloadUrl);

    private static IReadOnlyList<GitHubReleaseAsset> GetReleaseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<GitHubReleaseAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetPropertyOrDefault("name");
            var downloadUrl = asset.GetPropertyOrDefault("browser_download_url");
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                results.Add(new GitHubReleaseAsset(name, downloadUrl));
            }
        }

        return results;
    }

    private sealed record PendingUpdateEntry(
        string Id,
        UpdateTarget Target,
        string Name,
        string CurrentVersion,
        string LatestVersion,
        string? ReleaseUrl,
        Func<Task> Execute)
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Execute();
        }
    }

    private static string ApplyGithubProxy(string url)
    {
        if (string.IsNullOrWhiteSpace(_githubProxy)) return url;

        return _githubProxy.TrimEnd('/') + "/" + url;
    }
}

internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
