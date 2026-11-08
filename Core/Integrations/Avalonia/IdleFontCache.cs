using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;
using SkiaSharp;

namespace ShiroBot.Integrations.Avalonia;

/// <summary>Releases idle system font cache roots without invalidating retained controls.</summary>
internal sealed class IdleFontCache : IDisposable
{
    private static readonly Uri SystemFontsKey = new("fonts:SystemFonts");
    // Avalonia 12.1 keeps the shaper's native callback rooted even after removing a collection.
    // Guard these internals: an unsupported Avalonia version must keep its cache untouched.
    private static readonly FieldInfo? CollectionsField = typeof(FontManager)
        .GetField("_fontCollections", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? GlyphCacheField = typeof(FontManager).Assembly
        .GetType("Avalonia.Media.Fonts.FontCollectionBase")?
        .GetField("_glyphTypefaceCache", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ShaperField = typeof(GlyphTypeface)
        .GetField("_textShaperTypeface", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly TimeSpan _idleTimeout;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _log;
    private readonly List<WeakReference<GlyphTypeface>> _retired = [];
    private DispatcherTimer? _timer;
    private long _lastUse;
    private bool _trimmed;
    private bool _disabled;

    internal IdleFontCache(TimeSpan idleTimeout, TimeProvider? clock = null, Action<string>? log = null)
    {
        _idleTimeout = idleTimeout;
        _clock = clock ?? TimeProvider.System;
        _log = log;
        _lastUse = _clock.GetTimestamp();
    }

    internal static TimeSpan ConfiguredTimeout()
    {
        var value = Environment.GetEnvironmentVariable("SHIROBOT_FONT_CACHE_IDLE_SECONDS");
        return int.TryParse(value, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromMinutes(10);
    }

    internal void Start()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_idleTimeout <= TimeSpan.Zero || _timer is not null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Min(60, _idleTimeout.TotalSeconds))
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    internal void MarkUsed()
    {
        Dispatcher.UIThread.VerifyAccess();
        _lastUse = _clock.GetTimestamp();
        _trimmed = false;
    }

    private void OnTick(object? sender, EventArgs args) => TryTrim();

    internal int TryTrim(bool requestCollection = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disabled || _trimmed || _idleTimeout <= TimeSpan.Zero ||
            _clock.GetElapsedTime(_lastUse) < _idleTimeout) return 0;

        try
        {
            if (CollectionsField?.GetValue(FontManager.Current) is not ConcurrentDictionary<Uri, IFontCollection> collections ||
                GlyphCacheField is null || ShaperField is null || ShaperField.IsInitOnly)
                return Disable("当前 Avalonia 字体缓存结构不支持闲置回收，已跳过。");

            var fonts = new HashSet<GlyphTypeface>();
            collections.TryGetValue(SystemFontsKey, out var collection);
            if (collection is not null)
            {
                if (GlyphCacheField.GetValue(collection) is not IEnumerable entries)
                    return Disable("当前系统字体集合不支持闲置回收，已跳过。");
                foreach (var outer in entries)
                {
                    if (outer.GetType().GetProperty("Value")?.GetValue(outer) is not IEnumerable family)
                        return Disable("当前字体缓存条目不支持闲置回收，已跳过。");
                    foreach (var entry in family)
                        if (entry.GetType().GetProperty("Value")?.GetValue(entry) is GlyphTypeface font)
                            fonts.Add(font);
                }
            }
            foreach (var reference in _retired)
                if (reference.TryGetTarget(out var font)) fonts.Add(font);

            // Do not touch another text shaping backend or an unknown ownership contract.
            foreach (var font in fonts)
                if (ShaperField.GetValue(font) is { } shaper &&
                    (shaper is not IDisposable || shaper.GetType().FullName != "Avalonia.Harfbuzz.HarfBuzzTypeface"))
                    return Disable("当前排版后端不支持字体闲置回收，已跳过。");

            if (collection is not null && !collections.TryRemove(new KeyValuePair<Uri, IFontCollection>(SystemFontsKey, collection)))
                return 0;

            // Do NOT dispose the collection or platform typefaces: retained GlyphRuns/controls
            // still need them. Their SKNativeObject finalizers release them when no longer used.
            // Clearing the shaper's callback breaks the native -> managed reference cycle;
            // its lazy getter can recreate a shaper if a retained typeface is used again.
            _retired.Clear();
            foreach (var font in fonts)
            {
                (ShaperField.GetValue(font) as IDisposable)?.Dispose();
                ShaperField.SetValue(font, null);
                _retired.Add(new WeakReference<GlyphTypeface>(font));
            }
            _trimmed = true;
            if (fonts.Count == 0) return 0;
            SKGraphics.PurgeFontCache();
            SKGraphics.PurgeResourceCache();
            _log?.Invoke($"[渲染] 已释放闲置字体缓存引用：{fonts.Count} 个字体，下次渲染时按需重建。");
            if (requestCollection)
            {
                // Run after this stack has dropped its strong font references. Never wait for
                // finalizers on the UI thread; Skia finalizers can depend on other work finishing.
                Dispatcher.UIThread.Post(static () => GC.Collect(2, GCCollectionMode.Forced, blocking: false),
                    DispatcherPriority.Background);
            }
            return fonts.Count;
        }
        catch (Exception exception)
        {
            return Disable($"字体闲置回收已停止：{exception.GetType().Name}: {exception.Message}");
        }
    }

    private int Disable(string message)
    {
        _disabled = true;
        _timer?.Stop();
        _log?.Invoke("[渲染] " + message);
        return 0;
    }

    public void Dispose()
    {
        _disabled = true;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
        _retired.Clear();
    }
}
