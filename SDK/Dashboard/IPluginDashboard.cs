using System.Text.Json.Nodes;
using System.Globalization;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Dashboard;

/// <summary>
/// 插件在 Dashboard 里的页面。插件用 <see cref="Ui"/> 描述页面由哪些组件组成，Dashboard 用自己的组件渲染，
/// 不运行插件的 HTML 或脚本；按钮、表单、翻页和上传都由宿主转给插件的处理函数，插件不需要开放网页路由，
/// 也不需要自己做鉴权。通过 <see cref="IBotContext.Dashboard"/> 使用。
/// </summary>
public interface IPluginDashboard
{
    /// <summary>
    /// 添加一个页面。<paramref name="render"/> 按当前状态（筛选值、页码、标签页）生成组件树；
    /// 事件到来时宿主用同样的状态再渲染一次，按元素找到处理函数，所以渲染只应依赖状态和插件的数据。
    /// 返回的对象 Dispose 后移除页面；不移除也会随插件卸载注销。
    /// </summary>
    /// <param name="id">页面 ID，在插件内唯一。</param>
    /// <param name="title">导航里显示的标题。</param>
    /// <param name="render">生成页面的组件树。</param>
    /// <param name="icon">Material Symbols 图标名，例如 photo_library。</param>
    /// <param name="description">页面顶部的说明。</param>
    /// <param name="order">导航顺序，小的在前。</param>
    /// <param name="files">读取页面里引用的文件（<see cref="Ui.Image"/>、<see cref="UiCard.Image"/>）；不存在时返回 null。</param>
    IDisposable AddPage(
        string id,
        string title,
        Func<DashboardContext, CancellationToken, Task<UiNode>> render,
        string? icon = null,
        string? description = null,
        int order = 100,
        Func<string, CancellationToken, Task<DashboardFile?>>? files = null);
}

/// <summary>
/// 渲染和事件时的页面状态：筛选值、页码、当前标签页都由 Dashboard 保存，每次请求带上。
/// </summary>
public sealed class DashboardContext
{
    public DashboardContext(string pageId, IReadOnlyDictionary<string, string> state)
    {
        PageId = pageId;
        State = state;
    }

    public string PageId { get; }

    /// <summary>全部状态值。</summary>
    public IReadOnlyDictionary<string, string> State { get; }

    /// <summary>筛选值；为空或没有填写时为 null。</summary>
    public string? Filter(string key) =>
        State.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>开关筛选是否打开。</summary>
    public bool IsOn(string key) => string.Equals(Filter(key), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary><see cref="Ui.Pager"/> 的当前页码，从 1 开始。</summary>
    public int Page(string pagerId = "page") =>
        State.TryGetValue(pagerId, out var value) && int.TryParse(value, out var page) && page > 0 ? page : 1;

    /// <summary><see cref="Ui.Tabs"/> 的当前标签页 ID；还没切换过时为 null，Dashboard 显示第一个。</summary>
    public string? Tab(string tabsId) => State.TryGetValue(tabsId, out var value) && value.Length > 0 ? value : null;
}

/// <summary>一次用户操作：触发的元素和当时的页面状态。</summary>
public sealed class DashboardEvent
{
    public DashboardEvent(DashboardContext page, string elementId)
    {
        Page = page;
        ElementId = elementId;
    }

    public DashboardContext Page { get; }

    /// <summary>触发事件的按钮、表单、上传或数据组件的 ID。</summary>
    public string ElementId { get; }
}

/// <summary>表单提交的值，宿主已按字段声明检查过。</summary>
public sealed class DashboardForm
{
    public DashboardForm(JsonObject values) => Values = values;

    public JsonObject Values { get; }

    /// <summary>读取一个字段：字符串、数字、布尔或字符串数组；没有填写时为 default。</summary>
    public T? Get<T>(string key)
    {
        var node = Values[key];
        if (node is null) return default;
        object? value = typeof(T) switch
        {
            var t when t == typeof(string) => node.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString(),
            var t when t == typeof(bool) || t == typeof(bool?) => node.GetValue<bool>(),
            var t when t == typeof(int) || t == typeof(int?) => int.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            var t when t == typeof(long) || t == typeof(long?) => long.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            var t when t == typeof(double) || t == typeof(double?) => double.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            var t when t == typeof(decimal) || t == typeof(decimal?) => decimal.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            var t when t == typeof(string[]) => node.AsArray().Select(item => item?.GetValue<string>() ?? "").ToArray(),
            var t when t == typeof(List<string>) || t == typeof(IReadOnlyList<string>) =>
                node.AsArray().Select(item => item?.GetValue<string>() ?? "").ToList(),
            _ => throw new NotSupportedException($"表单字段不能读成 {typeof(T).Name}；用 Ui.Form<T> 读成 record。")
        };
        return (T?)value;
    }
}

/// <summary>
/// 处理函数的结果。默认成功后刷新页面；<see cref="Error"/> 只提示原因、不刷新。
/// </summary>
public sealed record DashboardResult
{
    public bool Ok { get; init; }

    /// <summary>提示给用户的文字。</summary>
    public string? Message { get; init; }

    /// <summary>是否重新渲染页面。</summary>
    public bool Refresh { get; init; }

    /// <summary>在居中对话框里显示的较长内容和它的标题。</summary>
    public string? Details { get; init; }

    public string? DetailsTitle { get; init; }

    /// <summary>要切换到的标签页：<c>(Tabs 的 ID, 标签页 ID)</c>。</summary>
    public (string TabsId, string TabId)? Tab { get; init; }

    /// <summary>要打开的插件页面和它的状态。</summary>
    public string? OpenPageId { get; init; }

    public IReadOnlyDictionary<string, string>? OpenPageState { get; init; }

    /// <summary>要以对话框打开的表单和预填的值。</summary>
    public string? OpenFormId { get; init; }

    public JsonObject? OpenFormValues { get; init; }

    /// <summary>成功：提示 <paramref name="message"/>（可省略）并刷新页面。</summary>
    public static DashboardResult Done(string? message = null) => new() { Ok = true, Message = message, Refresh = true };

    /// <summary>失败：提示原因，不刷新。</summary>
    public static DashboardResult Error(string message) => new() { Ok = false, Message = message };

    /// <summary>以对话框打开页面里声明为 <see cref="UiForm.AsDialog"/> 的表单，并预填 <paramref name="values"/>。</summary>
    public static DashboardResult OpenForm(string formId, JsonObject? values = null) =>
        new() { Ok = true, OpenFormId = formId, OpenFormValues = values };

    /// <summary>以对话框打开表单，按 record 预填。</summary>
    public static DashboardResult OpenForm<T>(string formId, T value) =>
        OpenForm(formId, PluginJson.ToNode(value) as JsonObject);

    public DashboardResult WithDetails(string title, string details) => this with { DetailsTitle = title, Details = details };

    public DashboardResult SwitchTab(string tabsId, string tabId) => this with { Tab = (tabsId, tabId) };

    /// <summary>打开插件的另一个页面，可以带上它的状态，例如筛选值。</summary>
    public DashboardResult OpenPage(string pageId, params (string Key, string Value)[] state) =>
        this with { OpenPageId = pageId, OpenPageState = state.ToDictionary(pair => pair.Key, pair => pair.Value) };

    public DashboardResult NoRefresh() => this with { Refresh = false };
}

/// <summary>上传文件的内容；文件名仅用于显示，不应作为磁盘路径。</summary>
public sealed record DashboardUploadFile(string FileName, string ContentType, ReadOnlyMemory<byte> Content);

/// <summary>
/// 交给 Dashboard 的文件：磁盘上的文件或内存中的内容。宿主在插件调用结束前打开独立的文件流或复制字节，不保留插件的对象。
/// </summary>
public sealed class DashboardFile
{
    private DashboardFile(string contentType, string? path, byte[]? content)
    {
        ContentType = contentType;
        Path = path;
        Content = content;
    }

    public string ContentType { get; }
    public string? Path { get; }
    public byte[]? Content { get; }

    public static DashboardFile FromPath(string path, string contentType) => new(contentType, path, null);

    public static DashboardFile FromBytes(byte[] content, string contentType) => new(contentType, null, content);
}
