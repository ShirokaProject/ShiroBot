using System.Collections.Concurrent;
using System.IO.Compression;
using ShiroBot.Components.Updates;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Events;
using ShiroBot.Plugins;

namespace ShiroBot.Update;

/// <summary>Package replacement shared by Dashboard installs, console updates and SDK update requests.</summary>
internal static class PluginUpdateService
{
    private const string DisabledPluginSuffix = ".disable";
    private const long MaxPluginUploadBytes = 100L * 1024L * 1024L;
    private const long MaxPluginExtractedBytes = 500L * 1024L * 1024L;
    private const int MaxPluginArchiveEntries = 4096;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks =
        new(StringComparer.OrdinalIgnoreCase);

    internal static SemaphoreSlim GetOperationLock(string id)
    {
        return OperationLocks.GetOrAdd(id.Trim(), _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>Updates an installed plugin from a release asset; the package must be the same plugin.</summary>
    internal static Task<PluginUpdateResult> ApplyAsync(
        PluginManager pluginManager, HostEventDispatcher eventDispatcher, PluginRouteConfig routePolicy,
        string id, string assetUrl, string assetName, string latestVersion,
        CancellationToken cancellationToken = default)
    {
        return FindInstalledPath(pluginManager, id) is null
            ? throw new InvalidOperationException($"未找到已安装插件: {id}")
            : InstallFromAssetAsync(pluginManager, eventDispatcher, routePolicy, assetUrl, assetName, null, id,
                cancellationToken);
    }

    /// <summary>
    ///     Downloads a plugin package and installs it, replacing an installed copy of the same plugin, then loads it.
    ///     Shared by the console's market install, plugin updates and automatic updates. A replacement that cannot be
    ///     made while the old version runs is staged for the next restart.
    /// </summary>
    /// <param name="sha256">Hex digest the download must match, as published by the plugin market.</param>
    /// <param name="expectedId">When set, a package for any other plugin is refused.</param>
    /// <param name="pluginManager">Host plugin registry and loader.</param>
    /// <param name="eventDispatcher">Dispatches events to the installed plugin.</param>
    /// <param name="routePolicy">Message routing policy for the installed plugin.</param>
    /// <param name="assetUrl">Download URL of the release asset.</param>
    /// <param name="assetName">Release asset filename used to identify the package format.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    internal static async Task<PluginUpdateResult> InstallFromAssetAsync(
        PluginManager pluginManager, HostEventDispatcher eventDispatcher, PluginRouteConfig routePolicy,
        string assetUrl, string assetName, string? sha256, string? expectedId,
        CancellationToken cancellationToken = default)
    {
        var uploadRoot = Path.Combine(Path.GetTempPath(), "ShiroBot", "plugin_updates", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(uploadRoot);
            var packagePath = Path.Combine(uploadRoot, Path.GetFileName(assetName));
            await Updater.DownloadFileAsync(assetUrl, packagePath, cancellationToken, MaxPluginUploadBytes, sha256)
                .ConfigureAwait(false);
            var package = PreparePluginUploadPackage(pluginManager, packagePath);
            var id = package.Info.Id;
            if (expectedId is not null && !string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"更新包的插件 ID {id} 与 {expectedId} 不一致。");

            var gate = GetOperationLock(id);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (StagedComponentUpdates.HasStagedDeletion(pluginManager.PluginRootPath, id))
                    throw new InvalidOperationException($"插件 {id} 已登记删除任务，请先重启宿主完成删除，再重新安装。");
                cancellationToken.ThrowIfCancellationRequested();

                var loaded = pluginManager.GetLoadedPluginSnapshot().FirstOrDefault(plugin =>
                    string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase));
                var installedPath = loaded?.AssemblyPath ?? FindInstalledPath(pluginManager, id);
                var version = package.Info.Version;
                if (installedPath is not null)
                {
                    var replacement = await ReplaceInstalledPluginAsync(pluginManager, eventDispatcher, routePolicy,
                        package,
                        new InstalledPluginInfo(installedPath, loaded?.Version ?? ""), loaded, true)
                        .ConfigureAwait(false);
                    if (replacement.PendingReason is not null)
                        return new PluginUpdateResult(true, $"插件 {id} 当前版本无法热替换，{version} 已暂存，将在下次重启宿主时替换",
                            replacement.PendingReason);
                    await LoadReplacementAsync(pluginManager, eventDispatcher, routePolicy, id, version, replacement,
                        loaded is not null).ConfigureAwait(false);
                    return new PluginUpdateResult(false, $"插件 {id} 已更新到 {version}");
                }

                InstallUploadedPlugin(pluginManager, package);
                await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, id).ConfigureAwait(false);
                return pluginManager.GetLoadedPluginSnapshot().All(plugin =>
                    !string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase))
                    ? throw new InvalidOperationException(
                        $"插件 {id} 的包已安装，但加载失败: {pluginManager.GetPluginLoadError(id) ?? "请检查宿主日志。"}")
                    : new PluginUpdateResult(false, $"插件 {id} {version} 已安装并加载");
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            TryDeleteDirectory(uploadRoot);
        }
    }

    /// <summary>
    ///     Loads a plugin whose files were just replaced. When the new version does not load, the previous files
    ///     are put back and, if it was running, the previous version is loaded again; the failure is then thrown.
    /// </summary>
    internal static async Task LoadReplacementAsync(
        PluginManager pluginManager, HostEventDispatcher eventDispatcher, PluginRouteConfig routePolicy,
        string id, string version, PluginReplacement replacement, bool wasLoaded)
    {
        await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, id).ConfigureAwait(false);
        if (IsLoaded())
        {
            replacement.Commit();
            return;
        }

        var loadError = pluginManager.GetPluginLoadError(id) ?? "请检查宿主日志";
        try
        {
            replacement.Rollback();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"插件 {id} 的新版本 {version} 加载失败（{loadError}），恢复旧版本文件也失败: {ex.Message}");
        }

        if (!wasLoaded)
            throw new InvalidOperationException($"插件 {id} 的新版本 {version} 加载失败，已恢复旧版本文件: {loadError}");
        await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, id).ConfigureAwait(false);
        throw new InvalidOperationException(IsLoaded()
            ? $"插件 {id} 的新版本 {version} 加载失败，已恢复并重新加载旧版本: {loadError}"
            : $"插件 {id} 的新版本 {version} 加载失败，旧版本文件已恢复但未能重新加载: {loadError}");

        bool IsLoaded()
        {
            return pluginManager.GetLoadedPluginSnapshot().Any(plugin =>
                string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string? FindInstalledPath(PluginManager pluginManager, string id)
    {
        return pluginManager.GetLoadedPluginSnapshot().FirstOrDefault(plugin =>
                   string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase))?.AssemblyPath
               ?? pluginManager.ResolveFirstPluginLoadCandidate(id);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static PluginUploadPackage PreparePluginUploadPackage(PluginManager pluginManager, string packagePath)
    {
        var extension = Path.GetExtension(packagePath);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var info = pluginManager.TryProbePluginInfoFile(packagePath)
                       ?? throw new InvalidOperationException("未找到 BotPluginAttribute，文件不是有效插件。");
            pluginManager.ValidateInstallCompatibility(packagePath);
            return new PluginUploadPackage(Path.GetDirectoryName(packagePath)!, packagePath, "dll", info);
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只支持上传 .dll 或 .zip 插件。");

        var extractRoot = Path.Combine(Path.GetDirectoryName(packagePath)!, "extract");
        Directory.CreateDirectory(extractRoot);
        ExtractZipSafely(packagePath, extractRoot);

        var pluginDlls = Directory.EnumerateFiles(extractRoot, "*.dll", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Info = pluginManager.TryProbePluginInfoFile(path) })
            .Where(item => item.Info is not null)
            .ToArray();

        var package = pluginDlls.Length switch
        {
            0 => throw new InvalidOperationException("压缩包中未找到有效插件 DLL。"),
            > 1 => throw new InvalidOperationException("压缩包中包含多个插件入口 DLL，请一次只上传一个插件。"),
            _ => new PluginUploadPackage(Path.GetDirectoryName(packagePath)!, pluginDlls[0].Path, "zip",
                pluginDlls[0].Info!)
        };
        var sourceRoot = GetZipInstallSourceRoot(package.RootPath, package.EntryAssemblyPath);
        var relativeEntry = Path.GetRelativePath(sourceRoot, package.EntryAssemblyPath);
        if (relativeEntry.Contains(Path.DirectorySeparatorChar) ||
            relativeEntry.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException("压缩包的插件入口 DLL 必须位于包根目录或唯一的顶层文件夹中。");

        pluginManager.ValidateInstallCompatibility(package.EntryAssemblyPath);
        return package;
    }

    /// <summary>
    ///     Replaces an installed plugin in place: only the package's files change, so the plugin's data and
    ///     config.toml stay. The package is staged first; when the running version cannot be unloaded or its
    ///     files are locked, the staged package waits for the next start and the current version keeps running.
    /// </summary>
    /// <param name="keepPrevious">
    ///     Keep the replaced files until the caller commits or rolls back the returned replacement; otherwise
    ///     they are dropped before returning.
    /// </param>
    internal static async Task<PluginReplacement> ReplaceInstalledPluginAsync(
        PluginManager pluginManager,
        HostEventDispatcher eventDispatcher,
        PluginRouteConfig routePolicy,
        PluginUploadPackage package,
        InstalledPluginInfo installed,
        LoadedPluginHandle? loaded,
        bool keepPrevious = false)
    {
        pluginManager.ValidateInstallCompatibility(package.EntryAssemblyPath);
        var root = pluginManager.PluginRootPath;
        var id = package.Info.Id;
        if (StagedComponentUpdates.HasStagedDeletion(root, id))
            throw new InvalidOperationException($"插件 {id} 已登记删除任务，请先重启宿主完成删除，再重新安装。");
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installedPath = Path.GetFullPath(installed.AssemblyPath);
        var installedDirectory =
            Path.GetDirectoryName(installedPath)!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isRootLevelFile = string.Equals(installedDirectory, rootFull, StringComparison.OrdinalIgnoreCase);
        // A plugin keeps its folder; a legacy DLL directly in the root moves into one named after its id.
        var targetDirectory = isRootLevelFile ? GetPluginInstallDirectory(root, id) : installedDirectory;

        var staging = StagedComponentUpdates.GetStagingDirectory(root, id);
        TryDeleteDirectory(staging);
        var stagedEntry = InstallUploadedPlugin(pluginManager, package, staging);
        StagedComponentUpdates.WriteTarget(staging, targetDirectory);
        if (isRootLevelFile) StagedComponentUpdates.WriteLegacyEntry(staging, installedPath);
        var entryPath = Path.Combine(targetDirectory, Path.GetRelativePath(staging, stagedEntry));

        if (loaded is not null &&
            !await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, loaded.Name).ConfigureAwait(false))
            return await KeepCurrentAsync("当前版本无法卸载（被其他插件依赖，或 OnUnload 执行失败）").ConfigureAwait(false);

        StagedComponentUpdates.PackageApplication applied;
        try
        {
            if (isRootLevelFile)
            {
                // Remove the old root-level file first, so a failure leaves nothing half-moved.
                pluginManager.SuppressWatcherPath(installedPath);
                File.Move(installedPath, installedPath + ".shirobot-old", true);
            }

            pluginManager.SuppressWatcherPath(targetDirectory);
            applied = StagedComponentUpdates.BeginApplyPackage(staging, targetDirectory);
            pluginManager.SuppressWatcherPath(entryPath);
            TryDeleteDirectory(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (isRootLevelFile && File.Exists(installedPath + ".shirobot-old") && !File.Exists(installedPath))
                File.Move(installedPath + ".shirobot-old", installedPath);
            return await KeepCurrentAsync(ex.Message).ConfigureAwait(false);
        }

        var replacement = new PluginReplacement(entryPath, null, Commit, Rollback);
        if (!keepPrevious) replacement.Commit();
        return replacement;

        void Commit()
        {
            applied.Commit();
            // A disabled install is a renamed copy of the entry DLL, not one of the package's files.
            if (!isRootLevelFile && installedPath.EndsWith(DisabledPluginSuffix, StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(installedPath);
            if (isRootLevelFile) TryDeleteFile(installedPath + ".shirobot-old");
        }

        void Rollback()
        {
            pluginManager.SuppressWatcherPath(targetDirectory);
            applied.Rollback();
            if (isRootLevelFile && File.Exists(installedPath + ".shirobot-old"))
            {
                pluginManager.SuppressWatcherPath(installedPath);
                File.Move(installedPath + ".shirobot-old", installedPath, true);
            }

            pluginManager.SuppressWatcherPath(targetDirectory);
        }

        static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        async Task<PluginReplacement> KeepCurrentAsync(string reason)
        {
            if (loaded is not null && pluginManager.GetLoadedPluginSnapshot().All(plugin =>
                    !string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase)))
                await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, id).ConfigureAwait(false);

            return new PluginReplacement(null, reason);
        }
    }

    /// <param name="targetRootOverride">Install somewhere else than the plugin's directory, e.g. a staging folder.</param>
    /// <param name="pluginManager">Host plugin registry and loader.</param>
    /// <param name="package">Prepared plugin package to install.</param>
    internal static string InstallUploadedPlugin(PluginManager pluginManager, PluginUploadPackage package,
        string? targetRootOverride = null)
    {
        pluginManager.ValidateInstallCompatibility(package.EntryAssemblyPath);
        var pluginRootPath = pluginManager.PluginRootPath;
        Directory.CreateDirectory(pluginRootPath);
        var targetRoot = targetRootOverride ?? GetPluginInstallDirectory(pluginRootPath, package.Info.Id);
        pluginManager.SuppressWatcherPath(targetRoot);
        if (package.Type.Equals("dll", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(targetRoot);
            var targetPath = Path.Combine(targetRoot, Path.GetFileName(package.EntryAssemblyPath));
            // The caller loads the plugin itself; without this the file watcher
            // would queue a second, redundant load for the same assembly.
            pluginManager.SuppressWatcherPath(targetPath);
            File.Copy(package.EntryAssemblyPath, targetPath, true);
            File.WriteAllLines(Path.Combine(targetRoot, StagedComponentUpdates.PackageFilesName),
                [Path.GetFileName(targetPath)]);
            pluginManager.SuppressWatcherPath(targetPath);
            pluginManager.SuppressWatcherPath(targetRoot);
            return targetPath;
        }

        var sourceRoot = GetZipInstallSourceRoot(package.RootPath, package.EntryAssemblyPath);
        StagedComponentUpdates.ApplyPackage(sourceRoot, targetRoot);
        // Restart the window so it is measured from the last write, not the first.
        pluginManager.SuppressWatcherPath(targetRoot);
        return Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, package.EntryAssemblyPath));
    }

    private static string GetPluginInstallDirectory(string pluginRootPath, string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId is "." or ".." ||
            pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            pluginId.Contains(Path.DirectorySeparatorChar) || pluginId.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException("插件 ID 不能用于安装目录名。");

        return Path.Combine(pluginRootPath, pluginId);
    }

    private static string GetZipInstallSourceRoot(string uploadRoot, string entryAssemblyPath)
    {
        var extractRoot = Path.Combine(uploadRoot, "extract");
        var relativeEntry = Path.GetRelativePath(extractRoot, entryAssemblyPath);
        var firstSeparator = relativeEntry.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (firstSeparator <= 0) return extractRoot;

        var firstSegment = relativeEntry[..firstSeparator];
        var topLevelDirectories = Directory.EnumerateDirectories(extractRoot).Select(Path.GetFileName).ToArray();
        var topLevelFiles = Directory.EnumerateFiles(extractRoot).ToArray();
        return topLevelDirectories.Length == 1 && topLevelFiles.Length == 0 &&
               string.Equals(topLevelDirectories[0], firstSegment, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(extractRoot, firstSegment)
            : extractRoot;
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        var normalizedDestination = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxPluginArchiveEntries)
            throw new InvalidOperationException($"压缩包文件数量超过限制: {archive.Entries.Count} > {MaxPluginArchiveEntries}");

        long totalUncompressedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxPluginExtractedBytes - totalUncompressedBytes)
                throw new InvalidOperationException($"压缩包解压后大小超过限制: {MaxPluginExtractedBytes}");
            totalUncompressedBytes += entry.Length;

            var destinationPath = Path.GetFullPath(Path.Combine(normalizedDestination, entry.FullName));
            if (!destinationPath.StartsWith(normalizedDestination + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destinationPath, normalizedDestination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("压缩包包含非法路径。");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, true);
        }
    }

    internal sealed record InstalledPluginInfo(string AssemblyPath, string Version);

    internal sealed record PluginUploadPackage(
        string RootPath,
        string EntryAssemblyPath,
        string Type,
        PluginProbeInfo Info);

    internal sealed record PluginUpdateResult(bool PendingRestart, string Message, string? Reason = null);

    /// <summary>
    ///     Outcome of replacing an installed plugin's files. With <c>keepPrevious</c>, call <see cref="Commit" />
    ///     once the new version runs or <see cref="Rollback" /> to put the previous files back.
    /// </summary>
    internal sealed record PluginReplacement(
        string? EntryPath,
        string? PendingReason,
        Action? CommitAction = null,
        Action? RollbackAction = null)
    {
        public void Commit()
        {
            CommitAction?.Invoke();
        }

        public void Rollback()
        {
            RollbackAction?.Invoke();
        }
    }
}