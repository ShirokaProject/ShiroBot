using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace ShiroBot.AvaloniaIntegration;

/// <summary>
/// 在专属后台线程上启动 Avalonia headless dispatcher。整个进程只允许有一个实例。
/// </summary>
internal sealed class AvaloniaHostBootstrapper : IDisposable
{
    private static readonly object SingletonLock = new();
    private static AvaloniaHostBootstrapper? _current;

    private readonly Thread _uiThread;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TaskCompletionSource<bool> _readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    private AvaloniaHostBootstrapper()
    {
        _uiThread = new Thread(UIThreadEntry)
        {
            IsBackground = true,
            Name = "Avalonia.UI"
        };
    }

    public static AvaloniaHostBootstrapper Start()
    {
        lock (SingletonLock)
        {
            if (_current is not null)
            {
                throw new InvalidOperationException("Avalonia host 已经启动过一次，整个进程只允许一个实例。");
            }

            var bootstrap = new AvaloniaHostBootstrapper();
            bootstrap._uiThread.Start();
            // 阻塞等到 dispatcher 就绪，避免后续 InvokeAsync 落在没初始化的 dispatcher 上。
            bootstrap._readySignal.Task.GetAwaiter().GetResult();
            _current = bootstrap;
            return bootstrap;
        }
    }

    private void UIThreadEntry()
    {
        try
        {
            AppBuilder.Configure<HeadlessHostApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false
                })
                .SetupWithoutStarting();

            _readySignal.TrySetResult(true);

            Dispatcher.UIThread.MainLoop(_shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 正常停机路径。
        }
        catch (Exception ex)
        {
            _readySignal.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _shutdownCts.Cancel();
        }
        catch
        {
            // ignore
        }

        // dispatcher 线程是后台线程，进程退出时会自动收掉，这里不强制 Join 避免阻塞退出。
        _shutdownCts.Dispose();

        lock (SingletonLock)
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }
}
