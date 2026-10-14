using System.Text.Json;

namespace ShiroBot.Components.Updates;

/// <summary>Keep failed shadow deletions separate from staged replacement packages.</summary>
internal static class DeferredShadowCleanup
{
    internal const string DirectoryName = ".shadow-cleanup";
    private const string MarkerName = "pending.json";
    private sealed record PendingShadow(string OriginalPath);

    internal static string? DeleteOrStage(string componentRoot, string shadowPath,
        Action<string>? deleteDirectory = null, Action<string, string>? moveDirectory = null)
    {
        deleteDirectory ??= path => Directory.Delete(path, recursive: true);
        moveDirectory ??= Directory.Move;
        ValidateOriginalPath(shadowPath);
        if (!Directory.Exists(shadowPath)) return null;
        try { deleteDirectory(shadowPath); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        var pending = Path.Combine(Path.GetFullPath(componentRoot), StagedComponentUpdates.DirectoryName,
            DirectoryName, Path.GetFileName(Path.GetFullPath(shadowPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        Directory.CreateDirectory(pending);
        // Write before attempting the move: a locked file or different volume may forbid moving.
        var marker = Path.Combine(pending, MarkerName);
        File.WriteAllText(marker + ".tmp", JsonSerializer.Serialize(new PendingShadow(Path.GetFullPath(shadowPath))));
        File.Move(marker + ".tmp", marker, overwrite: true);
        try { moveDirectory(shadowPath, Path.Combine(pending, "files")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return pending;
    }

    internal static (int Applied, IReadOnlyList<(string Path, string Error)> Failed) RetryPending(string componentRoot)
    {
        var root = Path.Combine(Path.GetFullPath(componentRoot), StagedComponentUpdates.DirectoryName, DirectoryName);
        if (!Directory.Exists(root)) return (0, []);
        int applied = 0;
        var failed = new List<(string, string)>();
        string[] directories;
        try { directories = Directory.GetDirectories(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, [(root, ex.Message)]);
        }
        foreach (var pending in directories)
        {
            if (!Guid.TryParseExact(Path.GetFileName(pending), "N", out _)) continue;
            try
            {
                var marker = Path.Combine(pending, MarkerName);
                var originalPath = File.Exists(marker)
                    ? (JsonSerializer.Deserialize<PendingShadow>(File.ReadAllText(marker))
                        ?? throw new InvalidOperationException("影子 DLL 清理记录无效。")).OriginalPath
                    : Path.Combine(Path.GetTempPath(), "ShiroBot", "plugin-reload", Path.GetFileName(pending));
                // Queue names preserve the original GUID, including if an interrupted write or
                // previous partial deletion removed the marker before cleanup finished.
                ValidateOriginalPath(originalPath);
                if (Directory.Exists(originalPath)) Directory.Delete(originalPath, recursive: true);
                Directory.Delete(pending, recursive: true);
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
            {
                failed.Add((pending, ex.Message));
            }
        }
        try { if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add((root, ex.Message)); }
        return (applied, failed);
    }

    private static void ValidateOriginalPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ShiroBot", "plugin-reload"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(fullPath), expectedParent, comparison) ||
            !Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _) ||
            (Directory.Exists(fullPath) && new DirectoryInfo(fullPath).LinkTarget is not null))
            throw new InvalidOperationException("拒绝清理影子 DLL 临时目录之外的路径。");
    }
}
