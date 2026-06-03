using Avalonia.Controls;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.AvaloniaSdk;

/// <summary>
/// 控件渲染选项。和 AxamlRenderOptions 平级，但用于直接传 Avalonia.Controls.Control 的场景。
/// </summary>
public sealed record ControlRenderOptions(
    int Width = 800,
    int Height = 600,
    double Dpi = 96)
{
    public static ControlRenderOptions Default { get; } = new();
}

/// <summary>
/// Avalonia 渲染上下文。继承自 IRenderContext，额外提供直接传 Control 的高级用法。
/// 仅当宿主以 EnableAvalonia=true 编译并启动 ShiroBot.AvaloniaIntegration 后，
/// IBotContext.Render 才能被强转成此接口。
/// </summary>
public interface IAvaloniaRenderContext : IRenderContext
{
    /// <summary>
    /// 在 Avalonia UI 线程上调用 factory 创建控件，渲染为 PNG 字节。
    /// factory 必须在每次调用都返回一个全新的控件实例（不要复用同一个根）。
    /// </summary>
    Task<byte[]> RenderControlPngAsync(
        Func<Control> factory,
        ControlRenderOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// RenderControlPngAsync 的便捷重载，写入临时文件并返回 file:// URI。
    /// </summary>
    Task<string> RenderControlPngToFileUriAsync(
        Func<Control> factory,
        ControlRenderOptions? options = null,
        CancellationToken ct = default);
}

public static class RenderContextExtensions
{
    /// <summary>
    /// 把 IRenderContext 强转成 Avalonia 渲染上下文。
    /// 当前宿主未启用 Avalonia 时会抛出 InvalidOperationException。
    /// </summary>
    public static IAvaloniaRenderContext AsAvalonia(this IRenderContext? renderer) =>
        renderer as IAvaloniaRenderContext
        ?? throw new InvalidOperationException(
            "当前没有 Avalonia 渲染服务可用。请确认宿主以 EnableAvalonia=true 编译，并已加载 ShiroBot.AvaloniaIntegration。");
}
