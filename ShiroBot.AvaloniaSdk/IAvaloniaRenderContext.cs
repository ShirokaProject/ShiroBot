using Avalonia.Controls;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.AvaloniaSdk;

/// <summary>
/// 控件渲染选项。和 AxamlRenderOptions 平级，但用于直接传 Avalonia.Controls.Control 的场景。
/// </summary>
public sealed record ControlRenderOptions(
    RenderTheme Theme = RenderTheme.Light,
    double Dpi = 192)
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
    /// 在 Avalonia UI 线程上创建并渲染控件实例。推荐用于 .axaml UserControl。
    /// </summary>
    Task<byte[]> RenderControlPngAsync<TControl>(
        object? dataContext = null,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
        where TControl : Control, new();

    /// <summary>
    /// 在 Avalonia UI 线程上创建并渲染控件实例，写入临时文件并返回 file:// URI。
    /// </summary>
    Task<string> RenderControlPngToFileUriAsync<TControl>(
        object? dataContext = null,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
        where TControl : Control, new();

}

public static class RenderContextExtensions
{
    /// <summary>
    /// 把 IRenderContext 强转成 Avalonia 渲染上下文。
    /// 当前宿主未启用 Avalonia 时会抛出 InvalidOperationException。
    /// </summary>
    private static IAvaloniaRenderContext AsAvalonia(this IRenderContext? renderer) =>
        renderer as IAvaloniaRenderContext
        ?? throw new InvalidOperationException(
            "当前没有 Avalonia 渲染服务可用。请确认宿主以 EnableAvalonia=true 编译，并已加载 ShiroBot.AvaloniaIntegration。");

    extension(IBotContext context)
    {
        /// <summary>
        /// 直接从 BotContext 在 Avalonia UI 线程上创建并渲染控件实例。
        /// </summary>
        public Task<byte[]> RenderControlPngAsync<TControl>(object? dataContext = null,
            ControlRenderOptions? options = null,
            CancellationToken ct = default)
            where TControl : Control, new() =>
            context.Render.AsAvalonia().RenderControlPngAsync<TControl>(dataContext, options, ct);

        /// <summary>
        /// 直接从 BotContext 在 Avalonia UI 线程上创建并渲染控件实例，返回 file:// URI。
        /// </summary>
        public Task<string> RenderControlPngToFileUriAsync<TControl>(object? dataContext = null,
            ControlRenderOptions? options = null,
            CancellationToken ct = default)
            where TControl : Control, new() =>
            context.Render.AsAvalonia().RenderControlPngToFileUriAsync<TControl>(dataContext, options, ct);

    }
}
