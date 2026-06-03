namespace ShiroBot.AvaloniaDemoPlugin.ViewModels;

/// <summary>
/// 演示用 ViewModel：纯 POCO，AXAML 通过 {Binding ...} 反射读属性。
/// 无需 INotifyPropertyChanged，因为这是一次性渲染场景。
/// </summary>
public sealed class ScreenshotViewModel
{
    public required string Title { get; init; }
    public required string RequestedBy { get; init; }
    public required string Timestamp { get; init; }
    public required string Footer { get; init; }
}
