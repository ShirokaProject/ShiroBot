using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Dashboard;

/// <summary>
/// 插件在 Dashboard 里的页面，用于配置表达不了的内容，例如浏览和管理插件的数据。
/// Dashboard 用通用组件渲染页面，不加载插件的 HTML 或脚本；数据、操作和文件都经过宿主已鉴权的 API，
/// 由宿主在进程内调用插件，插件不需要开放网页路由，也不需要自己做鉴权。
/// </summary>
public interface IPluginDashboardProvider
{
    /// <summary>插件的页面。每次打开插件时读取，可以随配置变化。</summary>
    IReadOnlyList<DashboardPage> Pages { get; }

    /// <summary>读取一页数据：<see cref="DashboardQuery"/> 带页码、每页数量和筛选值。统计页忽略分页。</summary>
    Task<DashboardPageData> QueryAsync(string pageId, DashboardQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 ID 读取当前条目，供宿主检查操作是否适用；不存在时返回 null。
    /// 声明操作的插件需要实现此方法，默认不允许执行条目操作。
    /// </summary>
    Task<DashboardItem?> GetItemAsync(string pageId, string itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult<DashboardItem?>(null);

    /// <summary>
    /// 对一个条目执行页面上声明的操作，<paramref name="itemId"/> 是条目 ID。
    /// 结果的 Message 提示给用户，Refresh 为 true 时 Dashboard 重新读取数据。
    /// </summary>
    Task<PluginActionResult> RunActionAsync(string pageId, string actionId, string itemId, CancellationToken cancellationToken = default);

    /// <summary>接收页面声明的文件上传；未实现时拒绝。内容由宿主限流读取，插件仍需检查实际格式。</summary>
    Task<PluginActionResult> UploadAsync(string pageId, DashboardUploadFile file, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PluginActionResult(false, "此页面不支持上传。"));

    /// <summary>读取条目引用的文件（例如图片）；不存在时返回 null。</summary>
    Task<DashboardFile?> OpenFileAsync(string pageId, string fileId, CancellationToken cancellationToken = default) =>
        Task.FromResult<DashboardFile?>(null);
}

/// <summary>页面的展示方式。</summary>
public static class DashboardPageKinds
{
    /// <summary>卡片网格，适合图片，例如表情库。</summary>
    public const string Gallery = "gallery";

    /// <summary>表格，列由 <see cref="DashboardPage.Columns"/> 声明，值取 <see cref="DashboardItem.Values"/>。</summary>
    public const string Table = "table";

    /// <summary>纵向列表。</summary>
    public const string List = "list";

    /// <summary>统计数字，数据取 <see cref="DashboardPageData.Metrics"/>。</summary>
    public const string Stats = "stats";
}

/// <param name="Id">页面 ID，在插件内唯一。</param>
/// <param name="Title">导航里显示的标题。</param>
/// <param name="Kind"><see cref="DashboardPageKinds"/> 之一。</param>
/// <param name="Icon">Material Symbols 图标名，例如 photo_library。</param>
/// <param name="Description">页面顶部的说明。</param>
/// <param name="Order">导航顺序，小的在前。</param>
/// <param name="Columns">表格的列。</param>
/// <param name="Filters">筛选项，值通过 <see cref="DashboardQuery.Filters"/> 传给插件。</param>
/// <param name="PageSize">每页数量；0 表示不分页。</param>
/// <param name="Actions">条目上可执行的操作；每个条目用 <see cref="DashboardItem.Actions"/> 指定它支持哪些。</param>
public sealed record DashboardPage(
    string Id,
    string Title,
    string Kind,
    string? Icon = null,
    string? Description = null,
    int Order = 100,
    IReadOnlyList<DashboardColumn>? Columns = null,
    IReadOnlyList<DashboardFilter>? Filters = null,
    int PageSize = 24,
    IReadOnlyList<DashboardAction>? Actions = null)
{
    /// <summary>为 null 时不允许上传；宿主单文件硬上限为 20 MB。</summary>
    public DashboardUpload? Upload { get; init; }
}

/// <summary>上传按钮、允许的 MIME 类型和每个文件的最大字节数。类型提示不能替代插件对实际内容的检查。</summary>
public sealed record DashboardUpload(string Label = "上传文件", IReadOnlyList<string>? ContentTypes = null,
    long MaxBytes = 5_000_000, string? Description = null);

/// <summary>上传文件的内容；文件名仅用于显示，不应作为磁盘路径。</summary>
public sealed record DashboardUploadFile(string FileName, string ContentType, ReadOnlyMemory<byte> Content);

public sealed record DashboardColumn(string Key, string Label);

/// <param name="Type">text 文本输入，select 下拉选择。</param>
public sealed record DashboardFilter(string Key, string Label, string Type = "text", IReadOnlyList<DashboardOption>? Options = null);

public sealed record DashboardOption(string Value, string Label);

/// <param name="Confirm">执行前的确认提示；为空时直接执行。</param>
/// <param name="Tone">default 或 danger。</param>
public sealed record DashboardAction(string Id, string Label, string? Confirm = null, string Tone = "default");

/// <param name="Page">从 1 开始的页码。</param>
/// <param name="PageSize">每页数量，不超过 100。</param>
/// <param name="Filters">筛选值，只包含非空的筛选项。</param>
public sealed record DashboardQuery(int Page, int PageSize, IReadOnlyDictionary<string, string> Filters)
{
    public string? Filter(string key) => Filters.TryGetValue(key, out var value) ? value : null;
}

/// <param name="Items">当前页的条目。</param>
/// <param name="Total">条目总数；不知道时为 null。</param>
/// <param name="HasMore">是否还有下一页。</param>
/// <param name="Metrics">统计页的数字。</param>
public sealed record DashboardPageData(
    IReadOnlyList<DashboardItem> Items,
    int? Total = null,
    bool HasMore = false,
    IReadOnlyList<DashboardMetric>? Metrics = null);

/// <param name="Id">条目 ID，执行操作时传回插件。</param>
/// <param name="Title">标题。</param>
/// <param name="Description">描述。</param>
/// <param name="ImageFileId">图片的文件 ID，Dashboard 通过 <see cref="IPluginDashboardProvider.OpenFileAsync"/> 读取。</param>
/// <param name="Tags">标签。</param>
/// <param name="Meta">次要信息，例如时间或数量。</param>
/// <param name="Values">表格各列的值。</param>
/// <param name="Actions">这个条目支持的操作 ID。</param>
public sealed record DashboardItem(
    string Id,
    string Title,
    string? Description = null,
    string? ImageFileId = null,
    IReadOnlyList<string>? Tags = null,
    string? Meta = null,
    IReadOnlyDictionary<string, string>? Values = null,
    IReadOnlyList<string>? Actions = null);

public sealed record DashboardMetric(string Label, string Value, string? Hint = null);

/// <summary>
/// 交给 Dashboard 的文件：磁盘上的文件或内存中的内容。宿主读完即释放，不保留插件的对象。
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
