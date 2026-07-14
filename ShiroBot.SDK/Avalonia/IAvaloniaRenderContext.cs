using Avalonia.Controls;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Avalonia;

/// <summary>
/// Options for rendering an Avalonia control in the host headless renderer.
/// </summary>
public sealed record ControlRenderOptions(
    RenderTheme Theme = RenderTheme.Light,
    double Dpi = 192)
{
    public static ControlRenderOptions Default { get; } = new();
}

/// <summary>
/// Avalonia rendering capabilities supplied by every ShiroBot host.
/// </summary>
public interface IAvaloniaRenderContext : IRenderContext
{
    Task<byte[]> RenderControlPngAsync<TControl>(
        object? dataContext = null,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
        where TControl : Control, new();

    Task<string> RenderControlPngToFileUriAsync<TControl>(
        object? dataContext = null,
        ControlRenderOptions? options = null,
        CancellationToken ct = default)
        where TControl : Control, new();
}

public static class AvaloniaRenderContextExtensions
{
    private static IAvaloniaRenderContext AsAvalonia(this IRenderContext? renderer) =>
        renderer as IAvaloniaRenderContext
        ?? throw new InvalidOperationException("Avalonia rendering is unavailable because host initialization failed.");

    extension(IBotContext context)
    {
        public Task<byte[]> RenderControlPngAsync<TControl>(
            object? dataContext = null,
            ControlRenderOptions? options = null,
            CancellationToken ct = default)
            where TControl : Control, new() =>
            context.Render.AsAvalonia().RenderControlPngAsync<TControl>(dataContext, options, ct);

        public Task<string> RenderControlPngToFileUriAsync<TControl>(
            object? dataContext = null,
            ControlRenderOptions? options = null,
            CancellationToken ct = default)
            where TControl : Control, new() =>
            context.Render.AsAvalonia().RenderControlPngToFileUriAsync<TControl>(dataContext, options, ct);
    }
}
