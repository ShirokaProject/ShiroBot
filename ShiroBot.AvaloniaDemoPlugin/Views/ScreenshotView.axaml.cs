using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ShiroBot.AvaloniaDemoPlugin.Views;

public partial class ScreenshotView : UserControl
{
    public ScreenshotView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
