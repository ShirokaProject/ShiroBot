using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ShiroBot.SDK.Avalonia;
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
            opts.Dpi,
            opts.Theme,
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

    public Task<byte[]> RenderControlPngAsync<TControl>(
        object? dataContext = null,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
        where TControl : Control, new()
    {
        var opts = options ?? ControlRenderOptions.Default;
        return RenderInternalAsync(
            () =>
            {
                var control = new TControl();
                if (dataContext is not null)
                {
                    control.DataContext = dataContext;
                }

                return control;
            },
            opts.Dpi,
            opts.Theme,
            ct);
    }

    public async Task<string> RenderControlPngToFileUriAsync<TControl>(
        object? dataContext = null,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
        where TControl : Control, new()
    {
        var bytes = await RenderControlPngAsync<TControl>(dataContext, options, ct).ConfigureAwait(false);
        return await WritePngAndBuildUriAsync(bytes, ct).ConfigureAwait(false);
    }

    private static Task<byte[]> RenderInternalAsync(
        Func<Control> controlFactory,
        double dpi,
        RenderTheme theme,
        CancellationToken ct)
    {
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI 必须为正数。");
        }

        return Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                AvaloniaIntegration.ApplyRenderTheme(theme);

                var content = controlFactory()
                    ?? throw new InvalidOperationException("控件工厂返回了 null。");

                var host = new Grid
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    ClipToBounds = false
                };
                host.Children.Add(content);
                var renderRoot = new Viewbox
                {
                    Stretch = Stretch.Fill,
                    Child = host
                };

                var window = new Window
                {
                    Width = 1,
                    Height = 1,
                    Content = renderRoot
                };

                try
                {
                    // 在 Window 上调 Show 让 visual tree attach + style/font/template 应用 + layout 计算。
                    // Headless 平台的 Window.Show 不会真的弹窗。
                    window.Show();

                    var constraint = ResolveMeasureConstraint(content);
                    content.Measure(constraint);
                    var desired = content.DesiredSize;
                    if (double.IsNaN(desired.Width) || double.IsInfinity(desired.Width) || desired.Width <= 0 ||
                        double.IsNaN(desired.Height) || double.IsInfinity(desired.Height) || desired.Height <= 0)
                    {
                        throw new InvalidOperationException("无法自动确定渲染尺寸。请在 AXAML 根控件上设置有限的 Width/Height，或确保内容能计算出有限的 DesiredSize。");
                    }

                    var finalWidth = ResolveFinalAxisSize(content.Width, content.MinWidth, content.MaxWidth, desired.Width);
                    var finalHeight = ResolveFinalAxisSize(content.Height, content.MinHeight, content.MaxHeight, desired.Height);

                    content.Width = finalWidth;
                    content.Height = finalHeight;
                    host.Width = finalWidth;
                    host.Height = finalHeight;
                    window.Width = finalWidth;
                    window.Height = finalHeight;

                    // 强制布局更新到目标尺寸。
                    var size = new Size(finalWidth, finalHeight);
                    host.Measure(size);
                    host.Arrange(new Rect(size));
                    window.Measure(size);
                    window.Arrange(new Rect(size));
                    window.UpdateLayout();

                    // 推动 headless 渲染时钟，确保至少完成一帧渲染。
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                    var renderScale = dpi / 96d;
                    var pixelWidth = (int)Math.Ceiling(finalWidth * renderScale);
                    var pixelHeight = (int)Math.Ceiling(finalHeight * renderScale);
                    renderRoot.Width = pixelWidth;
                    renderRoot.Height = pixelHeight;
                    window.Width = pixelWidth;
                    window.Height = pixelHeight;
                    renderRoot.Measure(new Size(pixelWidth, pixelHeight));
                    renderRoot.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
                    window.UpdateLayout();

                    using var frame = new RenderTargetBitmap(
                        new PixelSize(pixelWidth, pixelHeight),
                        new Vector(96, 96));
                    frame.Render(renderRoot);

                    using var ms = new MemoryStream();
                    frame.Save(ms, PngBitmapEncoderOptions.Default);
                    return ms.ToArray();
                }
                finally
                {
                    content.DataContext = null;
                    host.Children.Clear();
                    renderRoot.Child = null;
                    window.Content = null;
                    window.Close();
                }
            },
            DispatcherPriority.Render).GetTask();
    }

    private static Size ResolveMeasureConstraint(Control control)
    {
        var width = ResolveWidthMeasureConstraint(control.Width, control.MinWidth, control.MaxWidth);
        var height = ResolveHeightMeasureConstraint(control.Height, control.MaxHeight);
        return new Size(width, height);
    }

    private static double ResolveWidthMeasureConstraint(double value, double min, double max)
    {
        if (IsFinitePositive(value)) return value;
        if (IsFinitePositive(max)) return max;
        return double.PositiveInfinity;
    }

    private static double ResolveHeightMeasureConstraint(double value, double max)
    {
        if (IsFinitePositive(value)) return value;
        if (IsFinitePositive(max)) return max;
        return double.PositiveInfinity;
    }

    private static bool IsFinitePositive(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

    private static double ResolveFinalAxisSize(double value, double min, double max, double desired)
    {
        if (IsFinitePositive(value)) return value;

        var result = desired;
        if (IsFinitePositive(min)) result = Math.Max(result, min);
        if (IsFinitePositive(max)) result = Math.Min(result, max);
        return result;
    }

    private static async Task<string> WritePngAndBuildUriAsync(byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        return new Uri(path).AbsoluteUri;
    }
}
