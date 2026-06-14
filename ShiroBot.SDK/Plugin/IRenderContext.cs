namespace ShiroBot.SDK.Plugin;

public enum RenderTheme
{
    Light,
    Dark,
    Auto
}

/// <summary>
/// 通用渲染选项，所有 IRenderContext 实现都应支持。
/// </summary>
public sealed record AxamlRenderOptions(
    RenderTheme Theme = RenderTheme.Light,
    double Dpi = 192)
{
    public static AxamlRenderOptions Default { get; } = new();
}

/// <summary>
/// 渲染服务的最小接口，只用 BCL 类型，feature plugin 不需要直接引用渲染框架（例如 Avalonia）。
/// 真正的实现由宿主集成模块（例如 ShiroBot.AvaloniaIntegration）提供并通过 IBotContext.Render 暴露。
/// </summary>
public interface IRenderContext
{
    /// <summary>
    /// 渲染一段 AXAML 文本到 PNG。如果传入 dataContext，会作为根控件的 DataContext。
    /// </summary>
    Task<byte[]> RenderPngAsync(
        string axaml,
        object? dataContext = null,
        AxamlRenderOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 从磁盘读取 AXAML 文件后渲染。
    /// </summary>
    Task<byte[]> RenderPngFromFileAsync(
        string axamlPath,
        object? dataContext = null,
        AxamlRenderOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 渲染 PNG 后落盘到临时目录，返回 file:// URI。便于直接塞进 ImageOutgoingSegment。
    /// </summary>
    Task<string> RenderPngToFileUriAsync(
        string axaml,
        object? dataContext = null,
        AxamlRenderOptions? options = null,
        CancellationToken ct = default);
}
