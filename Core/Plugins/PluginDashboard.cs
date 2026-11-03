using System.Text.Json.Nodes;
using ShiroBot.Plugins.Dashboard;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Plugins;

internal sealed record PluginPageFile(Stream Content, string ContentType);

internal enum PluginPageStatus { Done, NotFound, Stale, InvalidForm, InvalidUpload, UploadTooLarge, Unloading, Failed }

/// <summary>页面操作的结果；<see cref="Message"/> 可以直接给用户看，表单不通过时 <see cref="Errors"/> 按字段给出原因。</summary>
internal sealed record PluginPageOutcome<T>(PluginPageStatus Status, string Message, T? Value = default,
    IReadOnlyDictionary<string, string>? Errors = null);

/// <summary>渲染好的页面：给 Dashboard 的组件树 JSON 和当时的状态。</summary>
internal sealed record PluginPageView(JsonObject View, IReadOnlyDictionary<string, string> State);

/// <summary>一次事件的结果：处理函数的结果、之后的状态，以及需要刷新时新的组件树。</summary>
internal sealed record PluginPageEventResponse(DashboardResult Result, IReadOnlyDictionary<string, string> State, JsonObject? View);

/// <summary>Dashboard 发来的事件：click 按钮、submit 表单、action 数据组件里条目的操作。</summary>
internal sealed record PluginPageEvent(string Kind, string Element, string? Action = null, string? Item = null, JsonObject? Values = null);

/// <summary>
/// 插件页面的渲染和事件。渲染、数据组件的行和处理函数都在插件内执行；事件到来时用同样的状态重新渲染，
/// 按元素 ID 找到处理函数，条目、表单值和上传文件都先由宿主检查。插件抛出的异常变成失败结果和原因。
/// </summary>
internal static partial class PluginDashboard
{
    internal const long MaxUploadBytes = 20_000_000;
    private const int MaxStateKeys = 64;
    private const int MaxStateValue = 2000;

    public static IReadOnlyList<DashboardPageRegistration> List(LoadedPluginHandle plugin) => plugin.Dashboard?.Pages ?? [];

    /// <summary>只保留合理大小的状态，避免把任意数据交给插件。</summary>
    public static Dictionary<string, string> CleanState(IEnumerable<KeyValuePair<string, string?>>? state) =>
        (state ?? [])
        .Where(pair => pair.Key.Length is > 0 and <= 100 && pair.Value is { Length: <= MaxStateValue })
        .Take(MaxStateKeys)
        .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

    public static Task<PluginPageOutcome<PluginPageView>> RenderAsync(LoadedPluginHandle plugin, string pageId,
        Dictionary<string, string> state, CancellationToken cancellationToken) =>
        RunAsync(plugin, pageId, "渲染页面", async page =>
        {
            if (page.Web is not null) return NotFound<PluginPageView>("请通过自定义网页接口打开这个页面。");
            var (view, _) = UiTreeWriter.Write(await page.Render(new DashboardContext(page.Id, state), cancellationToken).ConfigureAwait(false));
            return Done(new PluginPageView(view, state));
        });

    public static Task<PluginPageOutcome<PluginPageEventResponse>> EventAsync(LoadedPluginHandle plugin, string pageId,
        Dictionary<string, string> state, PluginPageEvent pageEvent, CancellationToken cancellationToken) =>
        RunAsync(plugin, pageId, "执行页面操作", async page =>
        {
            var context = new DashboardContext(page.Id, state);
            var (_, index) = UiTreeWriter.Write(await page.Render(context, cancellationToken).ConfigureAwait(false));
            if (!index.Elements.TryGetValue(pageEvent.Element, out var element)) return Stale<PluginPageEventResponse>();
            var e = new DashboardEvent(context, pageEvent.Element);

            Task<DashboardResult>? handler;
            switch (pageEvent.Kind, element)
            {
                case ("click", UiButton button):
                    handler = button.Invoke(e, cancellationToken);
                    break;
                case ("action", UiCollection collection) when pageEvent is { Action: { } action, Item: { } item }:
                    handler = collection.Invoke(action, item, e, cancellationToken);
                    break;
                case ("submit", UiForm { HasSubmit: true } form):
                    var (values, errors) = FormValidator.Validate(form, pageEvent.Values);
                    if (errors.Count > 0)
                        return new PluginPageOutcome<PluginPageEventResponse>(PluginPageStatus.InvalidForm, "表单有需要修改的地方。", Errors: errors);
                    handler = form.Invoke(new DashboardForm(values), e, cancellationToken);
                    break;
                default:
                    handler = null;
                    break;
            }
            if (handler is null) return Stale<PluginPageEventResponse>();
            return Done(await FinishAsync(page, state, await handler.ConfigureAwait(false), cancellationToken).ConfigureAwait(false));
        });

    /// <summary>上传：先渲染找到上传组件和它的限制，按限制读取请求体，再交给处理函数。</summary>
    public static Task<PluginPageOutcome<PluginPageEventResponse>> UploadAsync(LoadedPluginHandle plugin, string pageId,
        Dictionary<string, string> state, string elementId, string fileName, string contentType, Stream body, long? contentLength,
        CancellationToken cancellationToken) =>
        RunAsync(plugin, pageId, "上传文件", async page =>
        {
            var context = new DashboardContext(page.Id, state);
            var (_, index) = UiTreeWriter.Write(await page.Render(context, cancellationToken).ConfigureAwait(false));
            if (!index.Elements.TryGetValue(elementId, out var element) || element is not UiUpload upload)
                return Stale<PluginPageEventResponse>();

            var limit = Math.Min(upload.MaxBytes, MaxUploadBytes);
            if (contentLength > limit) return Upload<PluginPageEventResponse>(PluginPageStatus.UploadTooLarge, $"单个文件不能超过 {FormatBytes(limit)}。");
            var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
            if (name.Length is 0 or > 255) return Upload<PluginPageEventResponse>(PluginPageStatus.InvalidUpload, "文件名为空或过长。");
            var mime = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
            if (mime.Length == 0) mime = "application/octet-stream";
            if (upload.Accept.Count > 0 && !upload.Accept.Any(accepted => MatchesType(accepted, mime)))
                return Upload<PluginPageEventResponse>(PluginPageStatus.InvalidUpload, "不支持这种文件类型。");

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length + 1)), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > limit)
                    return Upload<PluginPageEventResponse>(PluginPageStatus.UploadTooLarge, $"单个文件不能超过 {FormatBytes(limit)}。");
                buffer.Write(chunk, 0, read);
            }
            if (buffer.Length == 0) return Upload<PluginPageEventResponse>(PluginPageStatus.InvalidUpload, "文件为空。");

            var result = await upload.Invoke(new DashboardUploadFile(name, mime, buffer.ToArray()), new DashboardEvent(context, elementId),
                cancellationToken).ConfigureAwait(false);
            return Done(await FinishAsync(page, state, result, cancellationToken).ConfigureAwait(false));
        });

    /// <summary>在插件调用结束前打开宿主拥有的流，文件删除或热卸载不影响正在发送的响应。</summary>
    public static Task<PluginPageOutcome<PluginPageFile>> OpenFileAsync(LoadedPluginHandle plugin, string pageId, string fileId,
        CancellationToken cancellationToken) =>
        RunAsync(plugin, pageId, "读取页面文件", async page =>
        {
            if (page.Files is null) return NotFound<PluginPageFile>("这个页面没有文件。");
            var file = await page.Files(fileId, cancellationToken).ConfigureAwait(false);
            if (file?.Path is { } path)
            {
                try
                {
                    return Done(new PluginPageFile(new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous), file.ContentType));
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    return NotFound<PluginPageFile>("文件不存在。");
                }
            }
            return file?.Content is { } content
                ? Done(new PluginPageFile(new MemoryStream(content.ToArray(), writable: false), file.ContentType))
                : NotFound<PluginPageFile>("文件不存在。");
        });

    /// <summary>应用结果里的标签页切换；需要刷新时用新的状态重新渲染。</summary>
    private static async Task<PluginPageEventResponse> FinishAsync(DashboardPageRegistration page, Dictionary<string, string> state,
        DashboardResult result, CancellationToken cancellationToken)
    {
        var next = new Dictionary<string, string>(state, StringComparer.Ordinal);
        if (result.Tab is { } tab) next[tab.TabsId] = tab.TabId;
        JsonObject? view = null;
        if (result.Refresh || result.Tab is not null)
            (view, _) = UiTreeWriter.Write(await page.Render(new DashboardContext(page.Id, next), cancellationToken).ConfigureAwait(false));
        return new PluginPageEventResponse(result, next, view);
    }

    private static async Task<PluginPageOutcome<T>> RunAsync<T>(LoadedPluginHandle plugin, string pageId, string operation,
        Func<DashboardPageRegistration, Task<PluginPageOutcome<T>>> work)
    {
        if (plugin.Dashboard?.Find(pageId) is not { } page) return NotFound<T>($"未找到页面: {pageId}");
        try
        {
            var (ran, outcome) = await plugin.Dashboard.RunAsync(() => work(page)).ConfigureAwait(false);
            return ran ? outcome! : new PluginPageOutcome<T>(PluginPageStatus.Unloading, $"插件 {plugin.Name} 正在卸载，请稍后再试。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            BotLog.Error($"{operation}失败: {plugin.Name}/{pageId} - {ex}");
            return new PluginPageOutcome<T>(PluginPageStatus.Failed, $"{operation}失败: {ex.Message}");
        }
    }

    private static bool MatchesType(string accepted, string mime) =>
        accepted.EndsWith("/*", StringComparison.Ordinal)
            ? mime.StartsWith(accepted[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(accepted, mime, StringComparison.OrdinalIgnoreCase);

    private static string FormatBytes(long bytes) =>
        bytes >= 1_000_000 ? $"{bytes / 1_000_000.0:0.#} MB" : bytes >= 1000 ? $"{bytes / 1000.0:0.#} KB" : $"{bytes} 字节";

    private static PluginPageOutcome<T> Done<T>(T value) => new(PluginPageStatus.Done, "", value);
    private static PluginPageOutcome<T> NotFound<T>(string message) => new(PluginPageStatus.NotFound, message);
    private static PluginPageOutcome<T> Upload<T>(PluginPageStatus status, string message) => new(status, message);
    private static PluginPageOutcome<T> Stale<T>() => new(PluginPageStatus.Stale, "页面内容已经变化，请刷新后再试。");
}
