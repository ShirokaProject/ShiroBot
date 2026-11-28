namespace ShiroBot.Hosting.Context;

internal sealed class ConfigWatchSubscription : IDisposable
{
    [ThreadStatic] private static ConfigWatchSubscription? _currentCallback;

    private readonly Action<ConfigWatchSubscription>? _onDisposed;
    private readonly SemaphoreSlim _reloadGate;
    private readonly Timer _timer;
    private readonly FileSystemWatcher _watcher;

    private int _disposed;
    private Action? _unsubscribe;

    internal ConfigWatchSubscription(FileSystemWatcher watcher, SemaphoreSlim reloadGate,
        Func<ConfigWatchSubscription, Task> reload, Action<ConfigWatchSubscription>? onDisposed = null)
    {
        _watcher = watcher;
        _reloadGate = reloadGate;
        _onDisposed = onDisposed;
        _timer = new Timer(state => _ = reload((ConfigWatchSubscription)state!), this,
            Timeout.Infinite, Timeout.Infinite);
    }

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Exception? disposeError = null;

        TryDispose(() => _watcher.EnableRaisingEvents = false);
        TryDispose(() => _unsubscribe?.Invoke());
        _unsubscribe = null;
        TryDispose(_timer.Dispose);

        // Dispose normally waits until a reload (including the plugin callback) has left the
        // critical section. A callback disposing its own subscription must not deadlock itself.
        if (!ReferenceEquals(_currentCallback, this))
            TryDispose(() =>
            {
                _reloadGate.Wait();
                _reloadGate.Release();
            });

        TryDispose(_watcher.Dispose);
        TryDispose(() => _onDisposed?.Invoke(this));

        if (disposeError is not null) throw disposeError;

        return;

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

    internal void SetUnsubscribe(Action unsubscribe)
    {
        _unsubscribe = unsubscribe;
    }

    internal void Start()
    {
        if (!IsDisposed) _watcher.EnableRaisingEvents = true;
    }

    internal void Schedule(int dueTime)
    {
        if (IsDisposed) return;

        try
        {
            _timer.Change(dueTime, Timeout.Infinite);
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
}