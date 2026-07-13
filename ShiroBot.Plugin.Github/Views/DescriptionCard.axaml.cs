using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ShiroBot.AvaloniaDemoPlugin.Views;

public partial class DescriptionCard : UserControl
{
    public DescriptionCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
