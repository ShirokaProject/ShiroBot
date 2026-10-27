using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using SimdSkia.Avalonia;

namespace ShiroBot.Integrations.Avalonia;

// Experiment-only switch. Both providers receive identical static font files.
internal static class LabRenderingBackend
{
    internal static bool UseOriginal => Environment.GetEnvironmentVariable("RENDER_LAB_BACKEND") == "skia";
    internal static bool MatchedFonts => Environment.GetEnvironmentVariable("RENDER_LAB_FONTS") == "matched";
    internal static AppBuilder Configure(AppBuilder builder)
    {
        if (UseOriginal) return builder.UseSkia();
        return builder.UseSimdSkia();
    }
    internal static void ApplyFonts(FontManager manager)
    {
        if (MatchedFonts)
        {
            manager.AddFontCollection(new EmbeddedFontCollection(new Uri("fonts:Lab"),new Uri("avares://ShiroBot/LabFonts")));
            var family = new FontFamily("fonts:Lab#Noto Sans SC");
            Application.Current!.Resources["ContentControlThemeFontFamily"] = family;
            Application.Current.Resources["Md3FontFamilyPlain"] = family;
            Application.Current.Resources["Md3FontFamilyBrand"] = family;
        }
        else SystemFontConfiguration.Apply(manager);
    }
}
