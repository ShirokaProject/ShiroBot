using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Styling;
using SimdSkia.Avalonia;

namespace ShiroBot.Integrations.Avalonia;

internal static class LabRenderingBackend
{
    internal static bool UseOriginal => Environment.GetEnvironmentVariable("RENDER_LAB_BACKEND") == "skia";
    private static bool MatchedFonts => Environment.GetEnvironmentVariable("RENDER_LAB_FONTS") == "matched";

    internal static AppBuilder Configure(AppBuilder builder) =>
        UseOriginal ? builder.UseSkia() : builder.UseSimdSkia();

    internal static void ApplyFonts(FontManager manager)
    {
        if (!MatchedFonts)
        {
            SystemFontConfiguration.Apply(manager);
            return;
        }

        manager.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts:Lab"), new Uri("avares://ShiroBot/LabFonts")));
        var family = new FontFamily("fonts:Lab#Noto Sans SC");
        var app = Application.Current!;
        app.Resources["ContentControlThemeFontFamily"] = family;
        app.Resources["Md3FontFamilyPlain"] = family;
        app.Resources["Md3FontFamilyBrand"] = family;
        app.Styles.Add(new Style(selector => selector.OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.FontFamilyProperty, family) }
        });
    }
}
