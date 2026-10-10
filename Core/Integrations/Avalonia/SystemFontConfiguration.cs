using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace ShiroBot.Integrations.Avalonia;

internal static class SystemFontConfiguration
{
    public static void Apply(FontManager fontManager)
    {
        var configured = Environment.GetEnvironmentVariable("SHIROBOT_DEFAULT_FONT_FAMILY");
        var platform = OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsWindows() ? OSPlatform.Windows : OSPlatform.Linux;
        var family = string.IsNullOrWhiteSpace(configured)
            ? SelectFamily(fontManager.SystemFonts.Select(x => x.Name), platform)
            : configured;
        if (family is null) return;

        var font = new FontFamily(family);
        // Fluent controls inherit this family. Also cover plugins that load
        // Material typography, whose family list overrides inherited fonts.
        Application.Current!.Resources["ContentControlThemeFontFamily"] = font;
        Application.Current.Resources["Md3FontFamilyPlain"] = font;
        Application.Current.Resources["Md3FontFamilyBrand"] = font;
    }

    internal static string? SelectFamily(IEnumerable<string> installed, OSPlatform platform)
    {
        // Keep mixed text (14.8万) and numeric-only text (7,305) in the same
        // system family so their line metrics and baselines match.
        string[] preferred = platform == OSPlatform.OSX ? ["PingFang SC", "Heiti SC"]
            : platform == OSPlatform.Windows ? ["Microsoft YaHei UI", "Microsoft YaHei", "DengXian"]
            : [];
        string[] cjk = ["Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC",
            "WenQuanYi Micro Hei", "WenQuanYi Zen Hei", "Droid Sans Fallback"];
        var available = installed.ToArray();
        foreach (var family in preferred.Concat(cjk))
        {
            var match = available.FirstOrDefault(x =>
                string.Equals(x, family, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return null;
    }
}
