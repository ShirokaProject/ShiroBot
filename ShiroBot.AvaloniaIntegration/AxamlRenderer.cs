using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ShiroBot.AvaloniaSdk;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.AvaloniaIntegration;

/// <summary>
/// Avalonia headless 渲染服务实现。所有渲染都串行在 Avalonia.UI 线程上执行。
///
/// 实现要点：把目标控件作为 Window.Content 加进 visual tree，让 styles / fonts / DataContext 正常 cascade，
/// 然后用 Headless 平台的 CaptureRenderedFrame 拿到位图，最后 Save 成 PNG。
/// </summary>
internal sealed class AxamlRenderer : IAvaloniaRenderContext
{
    private static readonly string TempRoot =
        Path.Combine(Path.GetTempPath(), "shirobot-render");

    public Task<byte[]> RenderPngAsync(
        string axaml,
        object? dataContext = null,
        AxamlRenderOptions? options = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(axaml))
        {
            throw new ArgumentException("AXAML 文本不能为空。", nameof(axaml));
        }

        var opts = options ?? AxamlRenderOptions.Default;
        return RenderInternalAsync(
            () =>
            {
                if (AvaloniaRuntimeXamlLoader.Load(axaml) is not Control control)
                {
                    throw new InvalidOperationException(
                        "AXAML 顶层节点必须是 Avalonia.Controls.Control 的子类。");
                }

                if (dataContext is not null)
                {
                    control.DataContext = dataContext;
                }

                return control;
            },
            opts.Width,
            opts.Height,
            opts.Dpi,
            ct);
    }

    public async Task<byte[]> RenderPngFromFileAsync(
        string axamlPath,
        object? dataContext = null,
        AxamlRenderOptions? options = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(axamlPath))
        {
            throw new ArgumentException("AXAML 文件路径不能为空。", nameof(axamlPath));
        }

        var text = await File.ReadAllTextAsync(axamlPath, ct).ConfigureAwait(false);
        return await RenderPngAsync(text, dataContext, options, ct).ConfigureAwait(false);
    }

    public async Task<string> RenderPngToFileUriAsync(
        string axaml,
        object? dataContext = null,
        AxamlRenderOptions? options = null,
        CancellationToken ct = default)
    {
        var bytes = await RenderPngAsync(axaml, dataContext, options, ct).ConfigureAwait(false);
        return await WritePngAndBuildUriAsync(bytes, ct).ConfigureAwait(false);
    }

    public Task<byte[]> RenderControlPngAsync(
        Func<Control> factory,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var opts = options ?? ControlRenderOptions.Default;
        return RenderInternalAsync(factory, opts.Width, opts.Height, opts.Dpi, ct);
    }

    public async Task<string> RenderControlPngToFileUriAsync(
        Func<Control> factory,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
    {
        var bytes = await RenderControlPngAsync(factory, options, ct).ConfigureAwait(false);
        return await WritePngAndBuildUriAsync(bytes, ct).ConfigureAwait(false);
    }

    private static Task<byte[]> RenderInternalAsync(
        Func<Control> controlFactory,
        int width,
        int height,
        double dpi,
        CancellationToken ct)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "宽高必须为正整数。");
        }

        return Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                ct.ThrowIfCancellationRequested();

                var content = controlFactory()
                    ?? throw new InvalidOperationException("控件工厂返回了 null。");

                var window = new Window
                {
                    Width = width,
                    Height = height,
                    Content = content
                };

                try
                {
                    // 在 Window 上调 Show 让 visual tree attach + style/font/template 应用 + layout 计算。
                    // Headless 平台的 Window.Show 不会真的弹窗。
                    window.Show();

                    // 强制布局更新到目标尺寸。
                    var size = new Size(width, height);
                    window.Measure(size);
                    window.Arrange(new Rect(size));
                    window.UpdateLayout();

                    // 推动 headless 渲染时钟，确保至少完成一帧渲染。
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                    using var frame = window.CaptureRenderedFrame()
                                      ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null。");

                    using var ms = new MemoryStream();
                    frame.Save(ms);
                    return ms.ToArray();
                }
                finally
                {
                    window.Close();
                }
            },
            DispatcherPriority.Render).GetTask();
    }

    private static async Task<string> WritePngAndBuildUriAsync(byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        return new Uri(path).AbsoluteUri;
    }
}
