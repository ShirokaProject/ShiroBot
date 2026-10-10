namespace ShiroBot.Hosting.Runtime;

/// <summary>Holds an OS-backed exclusive file handle for the host's entire lifetime.</summary>
internal sealed class HostDirectoryLock : IDisposable
{
    private readonly FileStream _stream;

    private HostDirectoryLock(FileStream stream) => _stream = stream;

    internal static HostDirectoryLock? TryAcquire(string hostDirectory)
    {
        var cacheDirectory = Path.Combine(Path.GetFullPath(hostDirectory), "cache");
        Directory.CreateDirectory(cacheDirectory);
        var path = Path.Combine(cacheDirectory, "host.lock");
        try
        {
            // OpenOrCreate preserves the inode: deleting/recreating a lock file would allow
            // another process to lock a different inode while the original host is still running.
            return new HostDirectoryLock(new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 11 or 32 or 33 or 35)
        {
            // EAGAIN/EWOULDBLOCK (Linux/macOS), sharing/lock violation (Windows).
            return null;
        }
    }

    // Never delete host.lock. Closing the handle (including process termination) releases the lock.
    public void Dispose() => _stream.Dispose();
}
