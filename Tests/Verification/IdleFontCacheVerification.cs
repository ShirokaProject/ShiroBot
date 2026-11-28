using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using ShiroBot.Integrations.Avalonia;

namespace ShiroBot.Verification;

internal static class IdleFontCacheVerification
{
    public static async Task RunTimerAsync(bool enabled)
    {
        Environment.SetEnvironmentVariable("SHIROBOT_FONT_CACHE_IDLE_SECONDS", enabled ? "1" : "0");
        using var host = AvaloniaHostBootstrapper.Start();
        var renderer = new AxamlRenderer();
        const string fixture =
            "<TextBlock xmlns=\"https://github.com/avaloniaui\" Width=\"500\" Height=\"100\" Text=\"闲置定时器测试 ABC\" FontStyle=\"Italic\" FontSize=\"24\"/>";
        var original = SHA256.HashData(await renderer.RenderPngAsync(fixture));
        await Task.Delay(400);
        _ = await renderer.RenderPngAsync(fixture);
        await Task.Delay(700);
        if (!await Dispatcher.UIThread.InvokeAsync(HasSystemCache))
            throw new Exception("Evicted a recently used font cache");
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            if (!await Dispatcher.UIThread.InvokeAsync(HasSystemCache)) break;
        }

        if (await Dispatcher.UIThread.InvokeAsync(HasSystemCache) == enabled)
            throw new Exception(enabled ? "Idle timer did not evict the cache" : "Disabled timer evicted the cache");
        if (!SHA256.HashData(await renderer.RenderPngAsync(fixture)).AsSpan().SequenceEqual(original))
            throw new Exception("Timer eviction changed rendered output");
        Console.WriteLine(
            $"PASS real idle timer enabled={enabled}, recent rendering resets timeout, identical PNG after idle");
    }

    private static bool HasSystemCache()
    {
        var collections = (IDictionary)typeof(FontManager)
            .GetField("_fontCollections", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(FontManager.Current)!;
        return collections.Contains(new Uri("fonts:SystemFonts"));
    }

    public static async Task RunAsync()
    {
        using var host = AvaloniaHostBootstrapper.Start();
        var clock = new TestClock();
        using var cache = new IdleFontCache(TimeSpan.FromMinutes(10), clock);
        var renderer = new AxamlRenderer();
        var fixtures = new[]
        {
            "<TextBlock xmlns=\"https://github.com/avaloniaui\" Width=\"600\" Height=\"110\" Text=\"网易云音乐 中文 ABC 123 AV\" FontFamily=\"PingFang SC\" FontSize=\"24\"/>",
            "<TextBlock xmlns=\"https://github.com/avaloniaui\" Width=\"600\" Height=\"110\" Text=\"播放量 14.8万 加粗字体\" FontFamily=\"PingFang SC\" FontSize=\"24\" FontWeight=\"SemiBold\"/>",
            "<TextBlock xmlns=\"https://github.com/avaloniaui\" Width=\"600\" Height=\"110\" Text=\"混排 倾斜 ABC 123\" FontFamily=\"PingFang SC\" FontSize=\"26\" FontWeight=\"Bold\" FontStyle=\"Italic\"/>",
            "<TextBlock xmlns=\"https://github.com/avaloniaui\" Width=\"600\" Height=\"110\" Text=\"回退字体 🌙 🎵 😀 café مرحبا\" FontSize=\"24\" TextWrapping=\"Wrap\"/>"
        };
        var expected = new List<byte[]>();
        foreach (var xaml in fixtures) expected.Add(SHA256.HashData(await renderer.RenderPngAsync(xaml)));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CachedTextControl.Layout = new TextLayout("保留的旧布局 AV 中文 🌙", new Typeface("PingFang SC", FontStyle.Italic),
                24, Brushes.Black);
            if (!FontManager.Current.TryGetGlyphTypeface(new Typeface("PingFang SC"), out var font))
                throw new Exception("Missing fixture font");
            CachedTextControl.Font = font;
            _ = font.TextShaperTypeface;
        });
        var retainedHash = SHA256.HashData(await renderer.RenderControlPngAsync<CachedTextControl>());
        await Dispatcher.UIThread.InvokeAsync(cache.MarkUsed);
        clock.Advance(TimeSpan.FromMinutes(9));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cache.TryTrim(false) != 0) throw new Exception("Evicted fonts before the idle timeout");
        });

        var beforeRss = Process.GetCurrentProcess().WorkingSet64;
        for (var cycle = 0; cycle < 20; cycle++)
        {
            clock.Advance(TimeSpan.FromMinutes(11));
            var weakFonts = await Dispatcher.UIThread.InvokeAsync(SnapshotFonts);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cache.TryTrim(false) == 0) throw new Exception("No idle fonts evicted");
                if (cache.TryTrim(false) != 0) throw new Exception("Repeated collection during one idle period");
            });
            Collect();
            if (weakFonts.Count > 1 && weakFonts.All(x => x.IsAlive))
                throw new Exception("All retired fonts remain rooted after eviction");

            var retained = SHA256.HashData(await renderer.RenderControlPngAsync<CachedTextControl>());
            if (!retained.AsSpan().SequenceEqual(retainedHash))
                throw new Exception("Retained text layout changed after eviction");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (CachedTextControl.Font!.PlatformTypeface.FamilyName.Length == 0 ||
                    CachedTextControl.Font.TextShaperTypeface is null)
                    throw new Exception("Retained typeface could not recreate its text shaper");
            });
            // Concurrent callers still serialize rendering on the UI dispatcher.
            var frames = await Task.WhenAll(fixtures.Select(x => renderer.RenderPngAsync(x)));
            for (var i = 0; i < frames.Length; i++)
                if (!SHA256.HashData(frames[i]).AsSpan().SequenceEqual(expected[i]))
                    throw new Exception($"PNG changed: cycle={cycle}, fixture={i}");
            await Dispatcher.UIThread.InvokeAsync(cache.MarkUsed);
            if (cycle % 5 == 0)
                Console.WriteLine(
                    $"cycle={cycle + 1} rss={Process.GetCurrentProcess().WorkingSet64} managed={GC.GetTotalMemory(false)}");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CachedTextControl.Layout!.Dispose();
            CachedTextControl.Layout = null;
            CachedTextControl.Font = null;
            clock.Advance(TimeSpan.FromMinutes(11));
            cache.TryTrim(false);
        });
        Collect();
        Console.WriteLine(
            $"PASS idle timeout, 20 evictions, 80 concurrent render requests, identical PNGs, retained layouts/typefaces; rss before={beforeRss} after={Process.GetCurrentProcess().WorkingSet64}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> SnapshotFonts()
    {
        var collection = FontManager.Current.SystemFonts;
        var field = collection.GetType().BaseType!.GetField("_glyphTypefaceCache",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fonts = new HashSet<GlyphTypeface>();
        foreach (var outer in (IEnumerable)field.GetValue(collection)!)
        foreach (var inner in (IEnumerable)outer.GetType().GetProperty("Value")!.GetValue(outer)!)
            if (inner.GetType().GetProperty("Value")!.GetValue(inner) is GlyphTypeface font)
                fonts.Add(font);
        return [.. fonts.Select(x => new WeakReference(x))];
    }

    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return _ticks;
        }

        public void Advance(TimeSpan elapsed)
        {
            _ticks += elapsed.Ticks;
        }
    }
}

internal sealed class CachedTextControl : Control
{
    internal static TextLayout? Layout;
    internal static GlyphTypeface? Font;

    public CachedTextControl()
    {
        Width = 600;
        Height = 110;
    }

    public override void Render(DrawingContext context)
    {
        Layout!.Draw(context, new Point(0, 0));
    }
}