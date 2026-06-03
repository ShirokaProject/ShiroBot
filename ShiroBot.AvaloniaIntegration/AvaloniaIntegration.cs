using ShiroBot.AvaloniaSdk;

namespace ShiroBot.AvaloniaIntegration;

/// <summary>
/// Avalonia 渲染集成模块。由宿主在启动时调用 <see cref="Initialize"/>，
/// 关闭时调用 <see cref="Shutdown"/>。整个进程只允许一个 Avalonia 实例。
///
/// 宿主把返回的 <see cref="IAvaloniaRenderContext"/> 通过 BotContext.AttachRenderer 暴露给所有插件。
/// </summary>
public static class AvaloniaIntegration
{
    private static readonly object Lock = new();
    private static AvaloniaHostBootstrapper? _bootstrap;
    private static AxamlRenderer? _renderer;

    /// <summary>
    /// 启动 Avalonia headless dispatcher 并返回渲染上下文。重复调用返回同一实例。
    /// </summary>
    public static IAvaloniaRenderContext Initialize()
    {
        lock (Lock)
        {
            if (_renderer is not null)
            {
                return _renderer;
            }

            _bootstrap = AvaloniaHostBootstrapper.Start();
            _renderer = new AxamlRenderer();
            return _renderer;
        }
    }

    /// <summary>
    /// 关停 Avalonia dispatcher。Avalonia 静态状态无法干净卸载，仅做尽力而为的资源释放。
    /// </summary>
    public static void Shutdown()
    {
        lock (Lock)
        {
            _bootstrap?.Dispose();
            _bootstrap = null;
            _renderer = null;
        }
    }
}
