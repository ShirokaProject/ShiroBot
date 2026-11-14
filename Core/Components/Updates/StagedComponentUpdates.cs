using System.Text.Json;

namespace ShiroBot.Components.Updates;

/// <summary>
///     Replaces an installed plugin or adapter package in place, and stages it for the next start when the
///     running copy cannot be released (a component that keeps its assembly referenced, a locked file).
///     <para>
///         Only files that belong to the package are replaced: the package's file list is recorded in
///         <see cref="PackageFilesName" />, so a later update removes files the new version dropped while
///         configuration and data the component created next to it are never touched.
///     </para>
/// </summary>
internal static class StagedComponentUpdates
{
    /// <summary>Staging folder inside the plugin or adapter root: <c>.update/&lt;id&gt;/</c>.</summary>
    public const string DirectoryName = ".update";

    public const string PackageFilesName = ".shirobot-package-files";

    private const string DeleteTargetFileName = ".shirobot-delete-target.json";

    private const string LegacyEntryFileName = ".shirobot-update-legacy-entry";

    private const string TargetFileName = ".shirobot-update-target";
    private const string BackupSuffix = ".shirobot-old";

    // Package defaults only seed a fresh install; existing user settings always win.
    private static readonly HashSet<string> PreservedFiles = new(StringComparer.OrdinalIgnoreCase) { "config.toml" };

    private static readonly HashSet<string> UserDirectories = new(StringComparer.OrdinalIgnoreCase)
        { "cookies", "data", "cache", "tmp", "logs" };

    public static string GetStagingDirectory(string componentRoot, string id)
    {
        return Path.Combine(Path.GetFullPath(componentRoot), DirectoryName, id);
    }

    public static bool IsStagingPath(string componentRoot, string path)
    {
        var staging = Path.Combine(Path.GetFullPath(componentRoot), DirectoryName) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(staging, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Deletion supersedes any package update staged for the same id.</summary>
    public static void StageDeletion(string componentRoot, string id, string targetPath, bool directory)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            id.Contains('/') || id.Contains('\\'))
            throw new InvalidOperationException("删除任务的组件 ID 无效。");
        var root = Path.GetFullPath(componentRoot);
        var target = Path.GetFullPath(targetPath);
        ValidateDeleteTarget(root, target);
        var staging = GetStagingDirectory(root, id);
        DiscardStaged(root, id);
        Directory.CreateDirectory(staging);
        var marker = Path.Combine(staging, DeleteTargetFileName);
        File.WriteAllText(marker + ".tmp", JsonSerializer.Serialize(new DeleteTarget(target, directory)));
        File.Move(marker + ".tmp", marker, true);
    }

    public static bool HasStagedDeletion(string componentRoot, string id)
    {
        return File.Exists(Path.Combine(GetStagingDirectory(componentRoot, id), DeleteTargetFileName));
    }

    /// <summary>
    ///     True when <paramref name="path" /> is, or lies inside, something recorded for deletion. Such a component
    ///     is uninstalled as far as the host is concerned: it is not listed and never loaded again, whether or not
    ///     its files could be removed yet.
    /// </summary>
    public static bool IsPendingDeletion(string componentRoot, string path)
    {
        var targets = GetStagedDeletionTargets(componentRoot);
        return targets.Count > 0 && IsPendingDeletion(targets, path);
    }

    public static bool IsPendingDeletion(IReadOnlyList<string> targets, string path)
    {
        var fullPath = Path.GetFullPath(path);
        return targets.Any(target => string.Equals(fullPath, target, StringComparison.OrdinalIgnoreCase) ||
                                     fullPath.StartsWith(target + Path.DirectorySeparatorChar,
                                         StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Full paths of the files and directories recorded for deletion under <paramref name="componentRoot" />.</summary>
    public static IReadOnlyList<string> GetStagedDeletionTargets(string componentRoot)
    {
        var stagingRoot = Path.Combine(Path.GetFullPath(componentRoot), DirectoryName);
        if (!Directory.Exists(stagingRoot)) return [];
        var targets = new List<string>();
        try
        {
            foreach (var staging in Directory.EnumerateDirectories(stagingRoot))
            {
                var marker = Path.Combine(staging, DeleteTargetFileName);
                if (!File.Exists(marker)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<DeleteTarget>(File.ReadAllText(marker)) is { Path.Length: > 0 } target)
                        targets.Add(Path.GetFullPath(target.Path)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                               or ArgumentException)
                {
                    // An unreadable marker is reported when the startup applier retries it.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return targets;
    }

    public static (IReadOnlyList<string> Applied, IReadOnlyList<(string Id, string Error)> Failed) ApplyStagedDeletions(
        string componentRoot)
    {
        var applied = new List<string>();
        var failed = new List<(string, string)>();
        var root = Path.GetFullPath(componentRoot);
        var stagingRoot = Path.Combine(root, DirectoryName);
        if (!Directory.Exists(stagingRoot)) return (applied, failed);
        foreach (var staging in Directory.EnumerateDirectories(stagingRoot))
        {
            var marker = Path.Combine(staging, DeleteTargetFileName);
            if (!File.Exists(marker)) continue;
            var id = Path.GetFileName(staging);
            try
            {
                var deletion = JsonSerializer.Deserialize<DeleteTarget>(File.ReadAllText(marker))
                               ?? throw new InvalidOperationException("暂存删除任务无效。");
                ValidateDeleteTarget(root, deletion.Path);
                if (deletion.Directory)
                {
                    if (Directory.Exists(deletion.Path)) Directory.Delete(deletion.Path, true);
                }
                else
                {
                    File.Delete(deletion.Path);
                }

                Directory.Delete(staging, true);
                applied.Add(id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or JsonException or ArgumentException)
            {
                failed.Add((id, ex.Message));
            }
        }

        if (!Directory.EnumerateFileSystemEntries(stagingRoot).Any()) Directory.Delete(stagingRoot);
        return (applied, failed);
    }

    private static void ValidateDeleteTarget(string root, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("删除任务缺少目标路径。");
        var fullTarget = Path.GetFullPath(target);
        var stagingRoot = Path.Combine(root, DirectoryName);
        if (!IsUnder(root, fullTarget) || IsStagingPath(root, fullTarget) ||
            string.Equals(fullTarget.TrimEnd(Path.DirectorySeparatorChar), stagingRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("拒绝删除组件目录之外或暂存目录中的文件。");
    }

    /// <summary>Records where a staged package goes, so the next start can apply it without other context.</summary>
    public static void WriteTarget(string stagingDirectory, string targetDirectory)
    {
        File.WriteAllText(Path.Combine(stagingDirectory, TargetFileName), Path.GetFullPath(targetDirectory));
    }

    public static void WriteLegacyEntry(string stagingDirectory, string entryPath)
    {
        File.WriteAllText(Path.Combine(stagingDirectory, LegacyEntryFileName), Path.GetFullPath(entryPath));
    }

    /// <summary>
    ///     Copies <paramref name="packageDirectory" /> over <paramref name="targetDirectory" />. Existing files are
    ///     renamed aside first (Windows allows renaming a loaded DLL, not overwriting it) and restored if any step
    ///     fails, so the installed copy is either fully replaced or left as it was.
    /// </summary>
    public static void ApplyPackage(string packageDirectory, string targetDirectory)
    {
        BeginApplyPackage(packageDirectory, targetDirectory).Commit();
    }

    /// <summary>
    ///     Like <see cref="ApplyPackage" />, but keeps the replaced files aside until the caller decides: commit once
    ///     the new version runs, or roll back to the previous files when it does not.
    /// </summary>
    public static PackageApplication BeginApplyPackage(string packageDirectory, string targetDirectory)
    {
        var source = Path.GetFullPath(packageDirectory);
        var target = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(target);

        var newFiles = EnumeratePackageFiles(source).ToArray();
        var previousFiles = ReadPackageFiles(target);
        // Never adopt an existing user file merely because a later package ships the same path.
        // Legacy installs without a manifest still need their program files replaced: assemblies, native
        // libraries and deps/runtimeconfig at any depth, or the new entry DLL runs against a stale deps.json.
        var legacyWithoutManifest = !File.Exists(Path.Combine(target, PackageFilesName));
        var collisions = newFiles.Where(file => File.Exists(Path.Combine(target, file)) &&
                                                !previousFiles.Contains(file, StringComparer.OrdinalIgnoreCase) &&
                                                !(legacyWithoutManifest && IsProgramFile(file)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedFiles = previousFiles.Except(newFiles, StringComparer.OrdinalIgnoreCase)
            .Where(file => !IsPreservedUserFile(file))
            .ToArray();

        var movedAside = new List<string>();
        var written = new List<string>();
        try
        {
            foreach (var relative in newFiles.Concat(removedFiles))
            {
                var path = Path.Combine(target, relative);
                if (!IsUnder(target, path) || !File.Exists(path)) continue;
                if (IsPreservedUserFile(relative) || collisions.Contains(relative)) continue;
                File.Move(path, path + BackupSuffix, true);
                movedAside.Add(path);
            }

            foreach (var relative in newFiles)
            {
                var destination = Path.Combine(target, relative);
                if (!IsUnder(target, destination)) continue;
                // Defaults must never overwrite credentials or configuration from an earlier install.
                if ((IsPreservedUserFile(relative) || collisions.Contains(relative)) &&
                    File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(source, relative), destination, false);
                written.Add(destination);
            }

            var manifestPath = Path.Combine(target, PackageFilesName);
            if (File.Exists(manifestPath))
            {
                File.Move(manifestPath, manifestPath + BackupSuffix, true);
                movedAside.Add(manifestPath);
            }

            written.Add(manifestPath);
            File.WriteAllLines(manifestPath,
                newFiles.Where(file => !IsPreservedUserFile(file) && !collisions.Contains(file)));
        }
        catch
        {
            foreach (var path in written) TryDelete(path);
            foreach (var path in movedAside)
                try
                {
                    File.Move(path + BackupSuffix, path, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

            throw;
        }

        return new PackageApplication(movedAside, written);
    }

    private static bool IsProgramFile(string relative)
    {
        if (IsPreservedUserFile(relative)) return false;
        var extension = Path.GetExtension(relative);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase) ||
               relative.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
               relative.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Applies every staged package under <paramref name="componentRoot" />. Runs at startup, before any
    ///     component is loaded, so nothing holds the files. Returns the ids applied and the ones that failed.
    /// </summary>
    public static (IReadOnlyList<string> Applied, IReadOnlyList<(string Id, string Error)> Failed) ApplyStaged(
        string componentRoot,
        Func<string, string?>? resolveTarget = null)
    {
        var applied = new List<string>();
        var failed = new List<(string, string)>();
        var root = Path.GetFullPath(componentRoot);
        CleanupBackups(root);

        var stagingRoot = Path.Combine(root, DirectoryName);
        if (!Directory.Exists(stagingRoot)) return (applied, failed);

        foreach (var staging in Directory.EnumerateDirectories(stagingRoot))
        {
            var id = Path.GetFileName(staging);
            if (id == DeferredShadowCleanup.DirectoryName) continue;
            if (File.Exists(Path.Combine(staging, DeleteTargetFileName))) continue;
            try
            {
                var markerPath = Path.Combine(staging, TargetFileName);
                var target = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : resolveTarget?.Invoke(id);
                if (string.IsNullOrWhiteSpace(target) || !IsUnder(root, target))
                    throw new InvalidOperationException("暂存的更新缺少有效的安装位置。");
                var legacyMarker = Path.Combine(staging, LegacyEntryFileName);
                var legacyEntry = File.Exists(legacyMarker) ? File.ReadAllText(legacyMarker).Trim() : null;
                if (legacyEntry is not null && (!IsUnder(root, legacyEntry) ||
                                                !string.Equals(Path.GetDirectoryName(legacyEntry), root,
                                                    StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("暂存的更新包含无效的旧入口路径。");
                var legacyMoved = legacyEntry is not null && File.Exists(legacyEntry);
                if (legacyMoved) File.Move(legacyEntry!, legacyEntry + BackupSuffix, true);
                try
                {
                    ApplyPackage(staging, target);
                }
                catch
                {
                    if (legacyMoved) File.Move(legacyEntry + BackupSuffix, legacyEntry!, true);
                    throw;
                }

                if (legacyMoved) TryDelete(legacyEntry + BackupSuffix);
                Directory.Delete(staging, true);
                applied.Add(id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failed.Add((id, ex.Message));
            }
        }

        if (!Directory.EnumerateFileSystemEntries(stagingRoot).Any()) Directory.Delete(stagingRoot);
        return (applied, failed);
    }

    public static bool HasStaged(string componentRoot, string id)
    {
        return Directory.Exists(GetStagingDirectory(componentRoot, id));
    }

    public static void DiscardStaged(string componentRoot, string id)
    {
        var staging = GetStagingDirectory(componentRoot, id);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
    }

    private static IEnumerable<string> EnumeratePackageFiles(string source)
    {
        return Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(source, file))
            .Where(file => !string.Equals(file, TargetFileName, StringComparison.Ordinal) &&
                           !string.Equals(file, PackageFilesName, StringComparison.Ordinal) &&
                           !string.Equals(file, LegacyEntryFileName, StringComparison.Ordinal) &&
                           !string.Equals(file, DeleteTargetFileName, StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal);
    }

    private static bool IsPreservedUserFile(string relative)
    {
        return PreservedFiles.Contains(Path.GetFileName(relative)) ||
               UserDirectories.Contains(relative.Replace('\\', '/').Split('/')[0]);
    }

    private static string[] ReadPackageFiles(string target)
    {
        var listPath = Path.Combine(target, PackageFilesName);
        if (!File.Exists(listPath)) return [];
        return
        [
            .. File.ReadAllLines(listPath)
                .Select(line => line.Trim())
                .Where(line =>
                    line.Length > 0 && !line.Contains("..", StringComparison.Ordinal) && !Path.IsPathRooted(line))
        ];
    }

    private static void CleanupBackups(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*" + BackupSuffix, SearchOption.AllDirectories))
            TryDelete(file);
    }

    private static bool IsUnder(string root, string path)
    {
        var normalizedRoot =
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record DeleteTarget(string Path, bool Directory);

    /// <summary>A package copied over an install whose replaced files are still kept next to the new ones.</summary>
    internal sealed class PackageApplication(IReadOnlyList<string> movedAside, IReadOnlyList<string> written)
    {
        private bool _completed;

        /// <summary>Drops the replaced files. Copies still locked by the process are removed at the next start.</summary>
        public void Commit()
        {
            if (_completed) return;
            _completed = true;
            foreach (var path in movedAside) TryDelete(path + BackupSuffix);
        }

        /// <summary>Removes the new files and puts the replaced ones back.</summary>
        public void Rollback()
        {
            if (_completed) return;
            _completed = true;
            // Files that only the new package brought are removed; replaced ones are moved back over the new copy.
            foreach (var path in written.Except(movedAside, StringComparer.OrdinalIgnoreCase)) TryDelete(path);
            List<Exception>? failures = null;
            foreach (var path in movedAside)
                try
                {
                    File.Move(path + BackupSuffix, path, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    (failures ??= []).Add(ex);
                }

            if (failures is not null) throw new IOException("恢复旧版本文件失败: " + failures[0].Message, failures[0]);
        }
    }
}