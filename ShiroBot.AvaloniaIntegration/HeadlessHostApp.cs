using Avalonia;
using Avalonia.Themes.Fluent;

namespace ShiroBot.AvaloniaIntegration;

/// <summary>
/// 仅承载样式资源的 Application。无窗口、无 lifetime，只为让运行时类型解析、字体管理器、
/// AvaloniaLocator 等静态状态完成初始化。
///
/// 类型公开是为了让 IDE 设计器（Avalonia previewer）能从宿主入口调
/// <see cref="AppBuilder.Configure{TApp}()"/> 实例化它。
/// </summary>
public sealed class HeadlessHostApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
