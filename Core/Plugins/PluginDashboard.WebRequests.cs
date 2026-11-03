using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Plugins;

internal static partial class PluginDashboard
{
    internal const long MaxWebTransferBytes = 1024L * 1024 * 1024;
    internal static readonly HashSet<string> WebRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Content-Type", "Content-Language", "Content-Range", "Accept", "Accept-Language", "Range", "If-Range", "If-Match", "If-None-Match", "If-Modified-Since", "If-Unmodified-Since" };
    private static readonly HashSet<string> WebResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Content-Type", "Content-Length", "Content-Disposition", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified" };

    internal static bool TryWebTarget(string target, out PathString path, out QueryString query)
    {
        path = default; query = default;
        if (target.Length is 0 or > 8192 || !target.StartsWith('/') || target.StartsWith("//", StringComparison.Ordinal) ||
            target.Any(c => char.IsControl(c) || c is '\\' or '#')) return false;
        var parts = target.Split('?', 2);
        string decoded;
        try { decoded = Uri.UnescapeDataString(parts[0]); } catch (UriFormatException) { return false; }
        if (decoded.Any(c => char.IsControl(c) || c is '\\' or ':' or '%' or '?' or '#') ||
            decoded.Split('/').Skip(1).Any(part => part is "." or "..") || decoded.Contains("//", StringComparison.Ordinal)) return false;
        path = new PathString(decoded);
        query = parts.Length == 2 ? new QueryString("?" + parts[1]) : QueryString.Empty;
        return true;
    }

    public static Task<PluginPageOutcome<bool>> WebRequestAsync(LoadedPluginHandle plugin, string pageId, string target, HttpContext http) =>
        RunAsync(plugin, pageId, "处理网页请求", async page =>
        {
            if (page.Web?.RequestAsync is not { } handler) return NotFound<bool>("这个页面没有 HTTP 处理函数。");
            if (!TryWebTarget(target, out var path, out var query))
                return new(PluginPageStatus.InvalidUpload, "页面内路径无效。");
            var limit = Math.Clamp(page.Web.MaxRequestBytes, 1, MaxWebTransferBytes);
            if (http.Request.ContentLength > limit) return new(PluginPageStatus.UploadTooLarge, "请求体超过页面大小限制。");
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = limit;
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, plugin.Dashboard!.Stopping);
            var ct = stopping.Token;
            ct.ThrowIfCancellationRequested();
            var response = new WebResponseFeature();
            var isolated = new DefaultHttpContext { RequestServices = http.RequestServices, RequestAborted = ct };
            isolated.Features.Set<IHttpResponseFeature>(response);
            isolated.Features.Set<IFormFeature>(new FormFeature(isolated.Request, new FormOptions
            {
                MultipartBodyLengthLimit = limit,
                MemoryBufferThreshold = 64 * 1024
            }));
            isolated.Request.Method = http.Request.Method;
            isolated.Request.Path = path;
            isolated.Request.QueryString = query;
            foreach (var (name, value) in http.Request.Headers)
                if (IsWebRequestHeader(name)) isolated.Request.Headers[name] = new StringValues(value.ToArray());
            isolated.Request.ContentLength = http.Request.ContentLength;
            isolated.Request.Body = new WebReadStream(http.Request.Body, limit, ct);
            var outputLimit = Math.Clamp(page.Web.MaxResponseBytes, 1, MaxWebTransferBytes);
            var output = new WebWriteStream(http.Response.Body, outputLimit, ct, async () =>
            {
                await response.StartAsync().ConfigureAwait(false);
                if (isolated.Response.ContentLength > outputLimit) throw new InvalidOperationException("网页响应超过页面大小限制。");
                http.Response.StatusCode = isolated.Response.StatusCode is >= 200 and <= 599 ? isolated.Response.StatusCode : 500;
                foreach (var (name, value) in isolated.Response.Headers)
                    if (WebResponseHeaders.Contains(name) || name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("X-Frame-Options", StringComparison.OrdinalIgnoreCase) && !name.Equals("X-Content-Type-Options", StringComparison.OrdinalIgnoreCase))
                        http.Response.Headers[name] = new StringValues(value.ToArray());
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.Remove("Set-Cookie");
                http.Response.Headers.Remove("Location");
            });
            isolated.Response.Body = output;
            try
            {
                var result = await handler(isolated.Request, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                await result.ExecuteAsync(isolated).ConfigureAwait(false);
                await output.StartAsync().ConfigureAwait(false);
                return Done(true);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == 413 && !http.Response.HasStarted)
            {
                http.Response.Headers.Clear();
                return new(PluginPageStatus.UploadTooLarge, "请求体超过页面大小限制。");
            }
            finally { await response.CompleteAsync().ConfigureAwait(false); }
        });

    internal static bool IsWebRequestHeader(string name) => WebRequestHeaders.Contains(name) ||
        name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("Auth", StringComparison.OrdinalIgnoreCase) && !name.Contains("Token", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("Key", StringComparison.OrdinalIgnoreCase);

    // DefaultHttpContext has no server to run completion callbacks. Multipart temp files
    // and Results.Stream registrations must still be disposed before plugin dispatch ends.
    private sealed class WebResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [], _completed = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }
        public void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) => _completed.Add((callback, state));
        public async Task StartAsync()
        {
            if (HasStarted) return;
            for (var i = _starting.Count - 1; i >= 0; i--) await _starting[i].Callback(_starting[i].State).ConfigureAwait(false);
            HasStarted = true;
        }
        public async Task CompleteAsync()
        {
            for (var i = _completed.Count - 1; i >= 0; i--) await _completed[i].Callback(_completed[i].State).ConfigureAwait(false);
        }
    }

    private abstract class WebTransferStream : Stream
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class WebReadStream(Stream inner, long limit, CancellationToken ct) : WebTransferStream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count)
        {
            ct.ThrowIfCancellationRequested();
            var n = inner.Read(buffer, offset, (int)Math.Min(count, limit - _read + 1));
            _read += n; if (_read > limit) throw new BadHttpRequestException("Request too large", 413); return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
            var n = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, limit - _read + 1)], linked.Token).ConfigureAwait(false);
            _read += n; if (_read > limit) throw new BadHttpRequestException("Request too large", 413); return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class WebWriteStream(Stream inner, long limit, CancellationToken ct, Func<Task> start) : WebTransferStream
    {
        private long _written;
        private bool _started;
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public async Task StartAsync() { if (!_started) { await start().ConfigureAwait(false); _started = true; } }
        public override void Flush() => FlushAsync(ct).GetAwaiter().GetResult();
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
            await StartAsync().ConfigureAwait(false);
            await inner.FlushAsync(linked.Token).ConfigureAwait(false);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask().GetAwaiter().GetResult();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_written + buffer.Length > limit) throw new InvalidOperationException("网页响应超过页面大小限制。");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
            linked.Token.ThrowIfCancellationRequested();
            await StartAsync().ConfigureAwait(false);
            await inner.WriteAsync(buffer, linked.Token).ConfigureAwait(false);
            _written += buffer.Length;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
