using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.Data;
using ShiroBot.Integrations.Avalonia;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class AvaloniaLohVerification
{
    public static async Task RunAsync()
    {
        using var host = AvaloniaHostBootstrapper.Start();
        using var pixels = new SKBitmap(768, 768);
        var source = new byte[768 * 768 * 4];
        new Random(12345).NextBytes(source);
        for (int i = 3; i < source.Length; i += 4) source[i] = 255;
        Marshal.Copy(source, 0, pixels.GetPixels(), source.Length);
        using var encoded = SKImage.FromBitmap(pixels);
        using var data = encoded.Encode(SKEncodedImageFormat.Png, 100);
        using var input = new MemoryStream(data.ToArray());
        using var bitmap = new Bitmap(input);
        const string axaml = "<Image xmlns=\"https://github.com/avaloniaui\" Width=\"768\" Height=\"768\" Source=\"{Binding}\"/>";
        var renderer = new AxamlRenderer();
        var expected = await renderer.RenderPngAsync(axaml, bitmap);
        if (expected.Length < 85000) throw new Exception("Fixture failed to exercise LOH-sized output.");
        var warm = new Uri(await renderer.RenderPngToFileUriAsync(axaml, bitmap)).LocalPath;
        File.Delete(warm);
        long before = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < 5; i++) await renderer.RenderPngAsync(axaml, bitmap);
        long buffered = GC.GetTotalAllocatedBytes(true) - before;
        before = GC.GetTotalAllocatedBytes(true);
        var files = new List<string>();
        try
        {
            for (int i = 0; i < 5; i++) files.Add(new Uri(await renderer.RenderPngToFileUriAsync(axaml, bitmap)).LocalPath);
            long streamed = GC.GetTotalAllocatedBytes(true) - before;
            var expectedHash = SHA256.HashData(expected);
            foreach (var path in files)
            {
                var actual = await File.ReadAllBytesAsync(path);
                if (!expectedHash.AsSpan().SequenceEqual(SHA256.HashData(actual)))
                    throw new Exception("Streamed PNG changed output.");
            }
            var controlPath = new Uri(await renderer.RenderControlPngToFileUriAsync<LohImageControl>(bitmap)).LocalPath;
            files.Add(controlPath);
            var controlBytes = await File.ReadAllBytesAsync(controlPath);
            if (!expectedHash.AsSpan().SequenceEqual(SHA256.HashData(controlBytes)))
                throw new Exception("Generic control file rendering changed PNG output.");
            if (streamed >= buffered / 2) throw new Exception("File rendering did not remove payload-sized buffers.");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await renderer.RenderPngToFileUriAsync(axaml, bitmap, ct: canceled.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
            Console.WriteLine($"PASS AvaloniaLohVerification.RunAsync: PNG={expected.Length}, buffered allocation={buffered}, file allocation={streamed}, five renders each; output SHA256 identical; cancellation passed.");
        }
        finally { foreach (var path in files) File.Delete(path); }
    }
}

internal sealed class LohImageControl : Image
{
    public LohImageControl()
    {
        Width = 768; Height = 768;
        Bind(SourceProperty, new Binding("."));
    }
}
