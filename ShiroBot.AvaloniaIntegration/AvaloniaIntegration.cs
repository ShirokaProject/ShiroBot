using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ShiroBot.AvaloniaSdk;
using ShiroBot.SDK.Plugin;

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
    private static string _themeMode = "Light";

    /// <summary>
    /// 启动 Avalonia headless dispatcher 并返回渲染上下文。重复调用返回同一实例。
    /// </summary>
    public static IAvaloniaRenderContext Initialize(string? themeMode = null)
    {
        lock (Lock)
        {
            if (!string.IsNullOrWhiteSpace(themeMode))
            {
                _themeMode = NormalizeThemeMode(themeMode);
            }

            if (_renderer is not null)
            {
                ApplyThemeMode();
                return _renderer;
            }

            _bootstrap = AvaloniaHostBootstrapper.Start();
            _renderer = new AxamlRenderer();
            ApplyThemeMode();
            return _renderer;
        }
    }

    public static void SetThemeMode(string? themeMode)
    {
        lock (Lock)
        {
            if (!string.IsNullOrWhiteSpace(themeMode))
            {
                _themeMode = NormalizeThemeMode(themeMode);
            }

            ApplyThemeMode();
        }
    }

    public static void ReleasePluginAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName)) return;

        try
        {
            AssetLoader.InvalidateAssemblyCache(assemblyName);
        }
        catch
        {
            // Best-effort cleanup for collectible plugin ALCs. Rendering must not depend on this succeeding.
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

    internal static ThemeVariant ResolveThemeVariant(string? themeMode = null)
    {
        var mode = NormalizeThemeMode(themeMode ?? _themeMode);
        return mode switch
        {
            "dark" => ThemeVariant.Dark,
            "auto" => IsDarkByClock() ? ThemeVariant.Dark : ThemeVariant.Light,
            _ => ThemeVariant.Light
        };
    }

    internal static void ApplyThemeMode()
    {
        if (Application.Current is null) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyThemeMode);
            return;
        }

        Application.Current!.RequestedThemeVariant = ResolveThemeVariant();
    }

    internal static void ApplyRenderTheme(RenderTheme theme)
    {
        if (Application.Current is null) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyRenderTheme(theme));
            return;
        }

        Application.Current!.RequestedThemeVariant = ResolveRenderThemeVariant(theme);
    }

    private static ThemeVariant ResolveRenderThemeVariant(RenderTheme theme)
    {
        return theme switch
        {
            RenderTheme.Dark => ThemeVariant.Dark,
            RenderTheme.Auto => IsDarkByClock() ? ThemeVariant.Dark : ThemeVariant.Light,
            _ => ThemeVariant.Light
        };
    }

    private static string NormalizeThemeMode(string? themeMode)
    {
        var value = (themeMode ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value)) return "Light";

        return value.ToLowerInvariant() switch
        {
            "dark" => "dark",
            "auto" => "auto",
            _ => "light"
        };
    }

    private static bool IsDarkByClock()
    {
        var hour = DateTime.Now.Hour;
        return hour >= 18 || hour < 6;
    }
}
