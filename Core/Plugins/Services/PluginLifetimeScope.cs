using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins.Services;

/// <summary>
/// 插件（或子作用域）登记的后台任务、计时器和资源。宿主分两步收尾：
/// <see cref="StopTasksAsync"/> 取消并等待后台任务（此时插件上下文仍可用），
/// 插件 OnUnload 之后再 <see cref="DisposeResourcesAsync"/> 按登记的相反顺序释放资源。
/// </summary>
internal sealed class PluginLifetimeScope : IPluginScope
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping;
    private readonly List<Cleanup> _cleanups = [];
    private readonly HashSet<Task> _tasks = [];
    private readonly HashSet<PluginLifetimeScope> _children = [];
    private readonly PluginLifetimeScope? _parent;
    private Task? _stopTasks;
    private Task? _disposeResources;
    // 任务停止后不再接受新任务；资源释放开始后不再接受新资源。OnUnload 期间仍可以 Track。
    private bool _tasksClosed;
    private bool _scopesClosed;
    private bool _resourcesClosed;

    public PluginLifetimeScope() : this(null)
    {
    }

    private PluginLifetimeScope(PluginLifetimeScope? parent)
    {
        _parent = parent;
        _stopping = parent is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(parent.Stopping);
    }

    public CancellationToken Stopping => _stopping.Token;

    /// <summary>宿主开始卸载插件：只发出取消信号，不等待也不释放。</summary>
    public void Cancel()
    {
        lock (_gate) _scopesClosed = true;
        try { _stopping.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException ex) { BotLog.Warning($"插件取消回调失败: {ex.InnerException?.Message ?? ex.Message}"); }
    }

    public T Track<T>(T resource) where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        Func<ValueTask> cleanup = resource switch
        {
            IAsyncDisposable asyncDisposable => asyncDisposable.DisposeAsync,
            IDisposable disposable => () => { disposable.Dispose(); return ValueTask.CompletedTask; },
            _ => throw new ArgumentException(
                $"{resource.GetType().FullName} implements neither IDisposable nor IAsyncDisposable.", nameof(resource))
        };
        AddCleanup(cleanup);
        return resource;
    }

    public IDisposable Defer(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        return AddCleanup(cleanup);
    }

    public Task Run(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Start(work, Stopping);
    }

    public IDisposable SetInterval(TimeSpan period, Func<CancellationToken, Task> tick)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(tick);
        return StartTimer(async token =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(period, token).ConfigureAwait(false);
                try { await tick(token).ConfigureAwait(false); }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    // 单次执行失败不停止计时器。
                    BotLog.Warning($"插件定时任务执行失败: {ex.Message}");
                }
            }
        });
    }

    public IDisposable SetTimeout(TimeSpan delay, Func<CancellationToken, Task> callback)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(callback);
        return StartTimer(async token =>
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            await callback(token).ConfigureAwait(false);
        });
    }

    public IPluginScope CreateScope()
    {
        lock (_gate)
        {
            if (_scopesClosed || _tasksClosed || _stopping.IsCancellationRequested) throw Closed();
            var child = new PluginLifetimeScope(this);
            _children.Add(child);
            return child;
        }
    }

    /// <summary>取消并等待本作用域和子作用域的后台任务、计时器结束；不释放资源。</summary>
    public Task StopTasksAsync()
    {
        lock (_gate) _stopTasks ??= StopTasksCoreAsync();
        return _stopTasks;
    }

    /// <summary>先停止任务，再按登记的相反顺序释放资源、执行清理回调和释放子作用域。</summary>
    public Task DisposeResourcesAsync()
    {
        lock (_gate) _disposeResources ??= DisposeResourcesCoreAsync();
        return _disposeResources;
    }

    public async ValueTask DisposeAsync() => await DisposeResourcesAsync().ConfigureAwait(false);

    private async Task StopTasksCoreAsync()
    {
        Cancel();
        PluginLifetimeScope[] children;
        lock (_gate) children = [.. _children];
        foreach (var child in children) await child.StopTasksAsync().ConfigureAwait(false);

        // 任务在等待期间还可能登记新的任务（例如 Run 里再 Run），直到没有剩余任务为止。
        while (true)
        {
            Task[] tasks;
            lock (_gate)
            {
                // 已完成的任务可能还没被移除回调删掉，不能再等它，否则会空转。
                tasks = [.. _tasks.Where(task => !task.IsCompleted)];
                if (tasks.Length == 0)
                {
                    _tasksClosed = true;
                    return;
                }
            }
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { /* 已在任务内记录 */ }
        }
    }

    private async Task DisposeResourcesCoreAsync()
    {
        await StopTasksAsync().ConfigureAwait(false);
        Cleanup[] cleanups;
        PluginLifetimeScope[] children;
        lock (_gate)
        {
            _resourcesClosed = true;
            cleanups = [.. _cleanups];
            _cleanups.Clear();
            children = [.. _children];
            _children.Clear();
        }

        foreach (var child in children) await child.DisposeResourcesAsync().ConfigureAwait(false);
        for (var i = cleanups.Length - 1; i >= 0; i--)
        {
            try { await cleanups[i].Run().ConfigureAwait(false); }
            catch (Exception ex) { BotLog.Warning($"插件资源释放失败: {ex.Message}"); }
        }

        if (_parent is not null) lock (_parent._gate) _parent._children.Remove(this);
        _stopping.Dispose();
    }

    private Cleanup AddCleanup(Func<ValueTask> cleanup)
    {
        var registration = new Cleanup(this, cleanup);
        lock (_gate)
        {
            if (_resourcesClosed) throw Closed();
            _cleanups.Add(registration);
        }
        return registration;
    }

    private IDisposable StartTimer(Func<CancellationToken, Task> loop)
    {
        var timer = CancellationTokenSource.CreateLinkedTokenSource(Stopping);
        var task = Start(loop, timer.Token);
        _ = task.ContinueWith(_ => timer.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new TimerHandle(timer);
    }

    /// <summary>返回任务本身，插件 await 时能看到异常；作用域另外记录未处理的异常，并在停止时等待它。</summary>
    private Task Start(Func<CancellationToken, Task> work, CancellationToken token)
    {
        Task task;
        lock (_gate)
        {
            if (_tasksClosed) throw Closed();
            task = Task.Run(() => work(token), CancellationToken.None);
            _tasks.Add(task);
        }
        _ = task.ContinueWith(completed =>
        {
            if (completed.Exception is { } error && !(token.IsCancellationRequested &&
                    error.InnerExceptions.All(e => e is OperationCanceledException)))
                BotLog.Warning($"插件后台任务失败: {error.InnerException?.Message ?? error.Message}");
            lock (_gate) _tasks.Remove(completed);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    private static ObjectDisposedException Closed() =>
        new(nameof(IPluginLifetime), "The plugin lifetime has already stopped.");

    private sealed class Cleanup(PluginLifetimeScope owner, Func<ValueTask> cleanup) : IDisposable
    {
        public ValueTask Run() => cleanup();

        public void Dispose()
        {
            lock (owner._gate) owner._cleanups.Remove(this);
        }
    }

    private sealed class TimerHandle(CancellationTokenSource timer) : IDisposable
    {
        public void Dispose()
        {
            try { timer.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }
}
