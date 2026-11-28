using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MyParser.Provider.BiliBili.Views;
using MyParser.Provider.Heybox.Views;
using ShiroBot.AvaloniaSdk;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.RssSubscriber.Presentation;
using Shirobot.Plugin.RssSubscriber.Presentation.Cards.X;
using SkiaSharp;

if (args is not [var output, var warmupCount, var sampleCount] ||
    !int.TryParse(warmupCount, CultureInfo.InvariantCulture, out var warmups) || warmups < 0 ||
    !int.TryParse(sampleCount, CultureInfo.InvariantCulture, out var samples) || samples < 1)
{
    Console.Error.WriteLine("Usage: CardBenchmark <output-directory> <warmups> <samples>");
    return 2;
}

Directory.CreateDirectory(output);
var prepare = typeof(AvaloniaIntegration).GetMethod(
    "PrepareRenderer", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(nameof(AvaloniaIntegration), "PrepareRenderer");
var renderer = (IAvaloniaRenderContext)prepare.Invoke(null, ["Light"])!;

using var cover = LoadBitmap(800, 450, false);
using var avatar = LoadBitmap(128, 128, true);
var rss = new CardViewModel
{
    Cover = cover, Avatar = avatar, FeedName = "山海观测站 · Mountain Desk",
    Title = "周末观测记录 · 山间晚霞与星空 😀",
    Description = "从山脚出发，沿步道走到观景台。今晚能见度不错，记录三种云层与天边的颜色变化。\n最后附上一张固定样图，供检查正文断行、头像和圆角。"
};
var heybox = new HeyboxCardViewModel
{
    Cover = cover, Title = "小黑盒周报：新地图实机体验 😀", AuthorName = "山海观测站",
    Description = "固定样例：检查中文正文、封面裁切、背景模糊与渐变。", StatsText = "1.2 万浏览 · 386 点赞 · 42 评论",
    MediaText = "图文", LinkId = "HB 20261128"
};
var live = new BiliLiveCardViewModel
{
    Cover = cover, Avatar = avatar, Title = "山间直播：一起等日落 😀", AnchorName = "山海观测站 · Live",
    RoomIdText = "房间号 123456", AudienceText = "1.2 万", WatchedText = "3.4 万",
    LiveStartTimeText = "18:30", LiveDurationText = "1 小时 24 分"
};
var article = new HeyboxArticleDocumentViewModel
{
    Cover = cover, Avatar = avatar, CanvasHeight = 1136, Title = "新地图体验：从山脚走到观测台 😀",
    AuthorName = "山海观测站", MetaText = "2026-11-28 · 小黑盒", StatsText = "1.2 万浏览 · 386 点赞",
    Blocks =
    [
        new() { Text = "路线与观测记录", FontSize = 22, FontWeight = "Bold", Foreground = "#899FB7", Margin = new Thickness(0, 8) },
        new() { Text = "沿着山谷向上走，路过两处废弃营地。太阳落到山脊以后，云层仍然保留了一点橙色。" +
                       "这里用同一张图检查正文插图、景深模糊、标题与段落换行。", Foreground = "#899FB7" },
        new() { IsImage = true, Image = cover, Height = 255, Caption = "图 1 · 山间晚霞" },
        new() { Text = "结束前，回头还能看见第一颗星。", Foreground = "#899FB7", Margin = new Thickness(0, 8) }
    ]
};
var cases = new List<(string Name, Func<ControlRenderOptions, Task<byte[]>> Render)>
{
    ("rss-post", options => renderer.RenderControlPngAsync<PostCard>(rss, options)),
    ("heybox-card", options => renderer.RenderControlPngAsync<HeyboxCard>(heybox, options)),
    ("bili-live", options => renderer.RenderControlPngAsync<BiliLiveCard>(live, options)),
    ("heybox-article", options => renderer.RenderControlPngAsync<HeyboxArticleDocument>(article, options))
};
using var preview = ExpandedCards.AddCases(cases, renderer, cover, avatar, output);
var results = new List<CardResult>();

try
{
    foreach (var theme in new[] { RenderTheme.Light, RenderTheme.Dark })
    {
        foreach (var (name, render) in cases)
        {
            var options = new ControlRenderOptions { Dpi = 192, Theme = theme };
            var sample = $"{name}-{theme.ToString().ToLowerInvariant()}";

            for (int iteration = 0; iteration < warmups; iteration++)
                await render(options);

            byte[] png = [];
            var times = new double[samples];
            for (int iteration = 0; iteration < samples; iteration++)
            {
                var start = Stopwatch.GetTimestamp();
                png = await render(options);
                times[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            File.WriteAllBytes(Path.Combine(output, $"{sample}.png"), png);
            using var decoded = SKBitmap.Decode(png) ?? throw new InvalidDataException($"Invalid PNG: {sample}");
            results.Add(new CardResult(sample, decoded.Width, decoded.Height, times));
            Console.WriteLine($"{sample,-23} {decoded.Width}x{decoded.Height}  {Median(times):F1} ms");
        }
    }

    var fonts = await Dispatcher.UIThread.InvokeAsync(InspectFonts);
    var report = new { fonts, cards = results };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    });
    File.WriteAllText(Path.Combine(output, "samples.json"), json);
}
finally
{
    AvaloniaIntegration.Shutdown();
}

return 0;

static double Median(double[] samples)
{
    var sorted = samples.Order().ToArray();
    return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
}

static object InspectFonts()
{
    var family = Application.Current!.Resources["ContentControlThemeFontFamily"] as FontFamily;
    var faces = new List<object>();
    if (Environment.GetEnvironmentVariable("RENDER_LAB_FONTS") == "matched")
    {
        foreach (int weight in new[] { 400, 500, 600, 700, 800, 900 })
        {
            var requested = new Typeface(family!, weight: (FontWeight)weight);
            if (!FontManager.Current.TryGetGlyphTypeface(requested, out var glyph) ||
                (int)glyph.Weight != weight || glyph.FontSimulations != FontSimulations.None ||
                !glyph.PlatformTypeface.TryGetStream(out var stream) || stream is null)
                throw new InvalidOperationException($"Could not resolve the real font face: {weight}");

            using (stream)
            {
                faces.Add(new { weight, sha256 = Convert.ToHexString(SHA256.HashData(stream)) });
            }
        }
    }

    return new { family = family?.ToString(), faces };
}

static Bitmap LoadBitmap(int width, int height, bool avatar)
{
    using var source = new SKBitmap(width, height);
    using var canvas = new SKCanvas(source);
    using var paint = new SKPaint { IsAntialias = true };
    paint.Shader = SKShader.CreateLinearGradient(new(0, 0), new(0, height),
        [new SKColor(27, 48, 96), new SKColor(226, 121, 104), new SKColor(250, 185, 107)], null, SKShaderTileMode.Clamp);
    canvas.DrawRect(0, 0, width, height, paint);
    paint.Shader = null;
    paint.Color = new SKColor(255, 233, 180);
    canvas.DrawCircle(width * .66f, height * .37f, height * .12f, paint);
    for (int layer = 0; layer < 4; layer++)
    {
        paint.Color = new SKColor((byte)(106 - layer * 24), (byte)(89 - layer * 15), (byte)(125 - layer * 11));
        using var path = new SKPath();
        for (int j = 0; j <= 16; j++)
        {
            var x = width * j / 16f;
            var y = height * (.52f + layer * .10f) + (float)Math.Sin(j * 1.2 + layer) * height * .09f;
            if (j == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        path.LineTo(width, height);
        path.LineTo(0, height);
        path.Close();
        canvas.DrawPath(path, paint);
    }
    if (avatar)
    {
        paint.Color = new SKColor(1, 29, 45);
        canvas.DrawCircle(width * .5f, height * .47f, height * .17f, paint);
        canvas.DrawOval(width * .5f, height * .92f, width * .29f, height * .36f, paint);
    }
    using var data = source.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = data.AsStream();
    return new Bitmap(stream);
}

sealed record CardResult(string Sample, int Width, int Height, double[] Times);
