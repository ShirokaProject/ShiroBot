using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShiroBot.Integrations.Avalonia;

internal static class AvaloniaFontVerification
{
    private const string Metrics = """
        <Border xmlns="https://github.com/avaloniaui" Width="288" Padding="4,9">
          <UniformGrid Columns="4">
            <StackPanel Spacing="2" HorizontalAlignment="Center">
              <TextBlock Text="播放" FontSize="9" HorizontalAlignment="Center" />
              <TextBlock Text="14.8万" FontSize="12" FontWeight="Bold" />
            </StackPanel>
            <StackPanel Spacing="2" HorizontalAlignment="Center">
              <TextBlock Text="点赞" FontSize="9" HorizontalAlignment="Center" />
              <TextBlock Text="2.6万" FontSize="12" FontWeight="Bold" />
            </StackPanel>
            <StackPanel Spacing="2" HorizontalAlignment="Center">
              <TextBlock Text="投币" FontSize="9" HorizontalAlignment="Center" />
              <TextBlock Text="7,305" FontSize="12" FontWeight="Bold" />
            </StackPanel>
            <StackPanel Spacing="2" HorizontalAlignment="Center">
              <TextBlock Text="收藏" FontSize="9" HorizontalAlignment="Center" />
              <TextBlock Text="9,142" FontSize="12" FontWeight="Bold" />
            </StackPanel>
          </UniformGrid>
        </Border>
        """;

    public static async Task RunAsync()
    {
        var cases = new (string[] Installed, OSPlatform Platform, string? Expected)[]
        {
            (["Segoe UI", "Microsoft YaHei UI"], OSPlatform.Windows, "Microsoft YaHei UI"),
            (["Microsoft YaHei", "Noto Sans CJK SC"], OSPlatform.Windows, "Microsoft YaHei"),
            (["DejaVu Sans", "Noto Sans CJK SC"], OSPlatform.Linux, "Noto Sans CJK SC"),
            (["WenQuanYi Micro Hei"], OSPlatform.Linux, "WenQuanYi Micro Hei"),
            (["DejaVu Sans"], OSPlatform.Linux, null),
            (["PingFang SC"], OSPlatform.OSX, "PingFang SC"),
            (["noto sans cjk sc"], OSPlatform.Linux, "noto sans cjk sc")
        };
        foreach (var item in cases)
        {
            if (SystemFontConfiguration.SelectFamily(item.Installed, item.Platform) != item.Expected)
                throw new InvalidOperationException($"Incorrect font selection for {item.Platform}.");
        }

        using var host = AvaloniaHostBootstrapper.Start();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var platform = OperatingSystem.IsMacOS() ? OSPlatform.OSX
                : OperatingSystem.IsWindows() ? OSPlatform.Windows : OSPlatform.Linux;
            var configured = Environment.GetEnvironmentVariable("SHIROBOT_DEFAULT_FONT_FAMILY");
            var expected = string.IsNullOrWhiteSpace(configured)
                ? SystemFontConfiguration.SelectFamily(FontManager.Current.SystemFonts.Select(x => x.Name), platform)
                : configured;
            if (expected is null)
            {
                Console.WriteLine("No supported system CJK font installed; preserving the platform default.");
                return;
            }
            foreach (var key in new[] { "ContentControlThemeFontFamily", "Md3FontFamilyPlain", "Md3FontFamilyBrand" })
            {
                if (Application.Current!.Resources[key] is not FontFamily family || family.Name != expected)
                    throw new InvalidOperationException($"Font resource {key} did not honor {expected}.");
            }

            var content = (Control)AvaloniaRuntimeXamlLoader.Load(Metrics);
            var window = new Window { Content = content, Width = 288, Height = 80 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var values = content.GetVisualDescendants().OfType<TextBlock>()
                    .Where(x => x.FontSize == 12).ToArray();
                var baselines = values.Select(x =>
                    x.TranslatePoint(new Point(0, 0), content)!.Value.Y + x.TextLayout.TextLines[0].Baseline).ToArray();
                if (values.Length != 4) throw new InvalidOperationException("Missing metric values.");
                // Explicit overrides are honored even if the chosen font lacks CJK glyphs.
                if (string.IsNullOrWhiteSpace(configured) && baselines.Max() - baselines.Min() > 0.01)
                    throw new InvalidOperationException($"Mixed and numeric-only baselines differ: {string.Join(", ", baselines)}.");
                Console.WriteLine($"Host font: {expected}; metric baselines: {string.Join(", ", baselines)}.");
            }
            finally { window.Close(); }
        });

        var png = await new AxamlRenderer().RenderPngAsync(Metrics);
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length <= signature.Length || !png.AsSpan(0, signature.Length).SequenceEqual(signature))
            throw new InvalidOperationException("Host renderer did not produce a PNG.");
        Console.WriteLine("Avalonia font selection, theme resources, and headless rendering verification passed.");
    }
}
