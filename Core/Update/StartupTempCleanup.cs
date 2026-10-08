namespace ShiroBot.Update;

/// <summary>Removes abandoned updater work before adapters and plugins start.</summary>
internal static class StartupTempCleanup
{
    internal static void Run(string hostRoot, string pluginRoot)
    {
        CleanHost(hostRoot);
        CleanPlugin(pluginRoot);
        Try(() =>
        {
            if (!IsRealDirectory(pluginRoot)) return;
            foreach (var directory in Directory.EnumerateDirectories(pluginRoot))
                if (!Path.GetFileName(directory).StartsWith('.') && IsRealDirectory(directory))
                    CleanPlugin(directory);
        });
    }

    private static void CleanHost(string root)
    {
        var temp = Path.Combine(root, ".tmp");
        var work = Path.Combine(temp, "ShiroBot.Update");
        Try(() =>
        {
            if (!IsRealDirectory(temp)) return;
            if (IsRealDirectory(work))
            {
                foreach (var directory in Directory.EnumerateDirectories(work))
                    if (Guid.TryParseExact(Path.GetFileName(directory), "N", out _) && IsRealDirectory(directory))
                        Try(() => DeleteWork(directory));
                DeleteEmpty(work);
            }
            DeleteEmpty(temp);
        });
    }

    private static void CleanPlugin(string root)
    {
        var temp = Path.Combine(root, ".tmp");
        Try(() =>
        {
            if (!IsRealDirectory(temp)) return;
            foreach (var file in Directory.EnumerateFiles(temp))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var separator = name.LastIndexOf('.');
                if (Path.GetExtension(file) is ".package" or ".replacement" or ".backup" &&
                    separator > 0 && Guid.TryParseExact(name[(separator + 1)..], "N", out _) &&
                    (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                    Try(() => File.Delete(file));
            }
            DeleteEmpty(temp);
        });
    }

    // Never follow links into user data, even inside an updater work directory.
    private static void DeleteWork(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            if ((attributes & FileAttributes.Directory) != 0) DeleteWork(entry);
            else File.Delete(entry);
        }
        DeleteEmpty(directory);
    }

    private static bool IsRealDirectory(string directory) => Directory.Exists(directory) &&
        (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0;

    internal static void DeleteEmpty(string directory)
    {
        Try(() =>
        {
            if (IsRealDirectory(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        });
    }

    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
