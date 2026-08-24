namespace ShiroBot.Hosting.Context;

internal sealed class ConfigWatchSubscription(
    FileSystemWatcher watcher,
    Timer timer,
    SemaphoreSlim reloadGate,
    Action unsubscribe,
    Action<ConfigWatchSubscription>? onDisposed = null) : IDisposable
{
    [ThreadStatic]
    private static ConfigWatchSubscription? _currentCallback;

    private int _disposed;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal void Start()
    {
        if (!IsDisposed)
        {
            watcher.EnableRaisingEvents = true;
        }
    }

    internal void Schedule(int dueTime)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            timer.Change(dueTime, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Disposal may race a file-system event already queued by the OS.
        }
    }

    internal void Invoke<T>(Action<T> callback, T value)
    {
        var previous = _currentCallback;
        _currentCallback = this;
        try
        {
            callback(value);
        }
        finally
        {
            _currentCallback = previous;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Exception? disposeError = null;

        TryDispose(() => watcher.EnableRaisingEvents = false);
        TryDispose(unsubscribe);
        TryDispose(timer.Dispose);

        // Dispose normally waits until a reload (including the plugin callback) has left the
        // critical section. A callback disposing its own subscription must not deadlock itself.
        if (!ReferenceEquals(_currentCallback, this))
        {
            TryDispose(() =>
            {
                reloadGate.Wait();
                reloadGate.Release();
            });
        }

        TryDispose(watcher.Dispose);
        TryDispose(() => onDisposed?.Invoke(this));

        if (disposeError is not null)
        {
            throw disposeError;
        }

        void TryDispose(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                disposeError ??= ex;
            }
        }
    }
}
