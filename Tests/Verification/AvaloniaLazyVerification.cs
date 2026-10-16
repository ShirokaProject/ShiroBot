using Avalonia;
using Avalonia.Media.Imaging;
using ShiroBot.Integrations.Avalonia;
using System.Security.Cryptography;
using SkiaSharp;

internal static class AvaloniaLazyVerification
{
    public static async Task RunAsync()
    {
        var renderer = AvaloniaIntegration.PrepareRenderer("Dark");
        if (Application.Current is not null) throw new Exception("Renderer preparation started Application.");
        // A plugin may decode its cover before asking the host to render a card.
        using (var pixels = new SKBitmap(1, 1))
        using (var image = SKImage.FromBitmap(pixels))
        using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var input = encoded.AsStream())
        using (var bitmap = new Bitmap(input))
            if (bitmap.PixelSize.Width != 1) throw new Exception("Pre-render bitmap decode failed.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        const string axaml = "<Border xmlns=\"https://github.com/avaloniaui\" Width=\"320\" Height=\"120\" Background=\"White\"><TextBlock Text=\"中文音乐测试 ABC 123\" FontSize=\"24\" FontStyle=\"Italic\"/></Border>";
        try { await renderer.RenderPngAsync(axaml, ct: canceled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        if (Application.Current is not null) throw new Exception("Canceled render initialized Application.");
        var images = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => renderer.RenderPngAsync(axaml))));
        var hash = SHA256.HashData(images[0]);
        if (images[0].Length < 1000 || images.Any(image => !hash.AsSpan().SequenceEqual(SHA256.HashData(image))))
            throw new Exception("Concurrent first renders produced inconsistent PNG output.");
        var eager = AvaloniaIntegration.Initialize("Light");
        if (!ReferenceEquals(renderer, eager)) throw new Exception("Renderer identity changed.");
        var again = await eager.RenderPngAsync(axaml);
        if (!hash.AsSpan().SequenceEqual(SHA256.HashData(again))) throw new Exception("Lazy initialization changed rendering.");
        AvaloniaIntegration.Shutdown();
        Console.WriteLine($"PASS lazy preparation, pre-render bitmap decoding, cancellation, eight concurrent first renders, eager API compatibility; PNG SHA256={Convert.ToHexString(hash)}");
    }
}
