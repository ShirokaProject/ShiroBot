using System.ComponentModel;
using System.Text.Json.Nodes;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Dashboard;

/// <summary>
/// 创建 Dashboard 页面组件。组件可以任意嵌套；参数里的 null 会被忽略，方便按条件放组件。
/// </summary>
public static class Ui
{
    // ---- 布局 ----

    /// <summary>纵向排列。</summary>
    public static UiNode Column(params UiNode?[] children) => new UiLayout("column", null, children);

    /// <summary>横向排列，窄屏自动换行。</summary>
    public static UiNode Row(params UiNode?[] children) => new UiLayout("row", null, children);

    /// <summary>带标题的分组卡片。</summary>
    public static UiNode Card(string title, params UiNode?[] children) => new UiLayout("card", title, children);

    /// <summary>标签页；当前标签页记在状态的 <paramref name="id"/> 里，用 <see cref="DashboardContext.Tab"/> 读取。</summary>
    public static UiNode Tabs(string id, params UiTab[] tabs) => new UiTabs(id, tabs);

    public static UiTab Tab(string id, string title, params UiNode?[] children) => new(id, title, children);

    public static UiNode Divider() => new UiDivider();

    // ---- 内容 ----

    public static UiNode Text(string text) => new UiText(text, markdown: false);

    /// <summary>Markdown 文本，只支持基本格式，不渲染 HTML。</summary>
    public static UiNode Markdown(string markdown) => new UiText(markdown, markdown: true);

    /// <summary>提示条：info、success、warning 或 danger。</summary>
    public static UiNode Notice(string text, string tone = "info") => new UiNotice(text, tone);

    /// <summary>一个统计数字。</summary>
    public static UiNode Metric(string label, string value, string? hint = null) => new UiMetric(label, value, hint);

    /// <summary>图片，通过 AddPage 的 files 读取。</summary>
    public static UiNode Image(string fileId, string? alt = null) => new UiImage(fileId, alt);

    /// <summary>等宽文本，例如日志或配置片段。</summary>
    public static UiNode Code(string text) => new UiCode(text);

    /// <summary>链接：<paramref name="page"/> 跳到插件的另一个页面，<paramref name="url"/> 打开外部地址（只允许 http/https）。</summary>
    public static UiNode Link(string text, string? page = null, string? url = null) => new UiLink(text, page, url);

    /// <summary>空状态；<paramref name="when"/> 为 false 时不显示。</summary>
    public static UiNode? Empty(string text, bool when = true) => when ? new UiEmpty(text) : null;

    // ---- 数据 ----

    /// <summary>表格；<paramref name="key"/> 是每行的唯一标识，操作处理函数收到对应的条目。</summary>
    public static UiTable<T> Table<T>(IEnumerable<T> items, Func<T, string> key) => new(items, key);

    /// <summary>卡片网格，适合图片。</summary>
    public static UiCards<T> Gallery<T>(IEnumerable<T> items, Func<T, UiCard> card) => new("gallery", items, card);

    /// <summary>纵向列表。</summary>
    public static UiCards<T> List<T>(IEnumerable<T> items, Func<T, UiCard> card) => new("list", items, card);

    /// <summary>筛选栏，值记在状态里，用 <see cref="DashboardContext.Filter"/> 读取。</summary>
    public static UiNode Filters(params UiFilter[] filters) => new UiFilters(filters);

    public static UiFilter Input(string key, string label) => new(key, label, "input", []);

    public static UiFilter Select(string key, string label, IEnumerable<(string Value, string Label)> options) =>
        new(key, label, "select", options.Select(option => new UiOption(option.Value, option.Label)).ToArray());

    public static UiFilter Switch(string key, string label) => new(key, label, "switch", []);

    /// <summary>分页，页码记在状态的 <paramref name="id"/> 里；不知道总数时传 <paramref name="hasMore"/>。</summary>
    public static UiNode Pager(int? total, int pageSize, string id = "page", bool? hasMore = null) =>
        new UiPager(id, total, pageSize, hasMore);

    // ---- 交互 ----

    public static UiNode Button(string id, string label, Func<DashboardEvent, CancellationToken, Task<DashboardResult>> handler,
        string? confirm = null, bool danger = false) => new UiButton(id, label, handler, confirm, danger);

    /// <summary>表单，逐个声明字段。</summary>
    public static UiForm Form(string id) => new(id);

    /// <summary>按 record 生成表单：类型、默认值、[Description]、[Range]、[MaxLength]、[UiField] 和必填都取自 record。</summary>
    public static UiForm<T> Form<T>(string id) => new(id);

    /// <summary>上传文件；宿主按 <paramref name="maxBytes"/>（不超过 20 MB）限流读取，插件仍需检查实际内容。</summary>
    public static UiNode Upload(string id, string label, Func<DashboardUploadFile, DashboardEvent, CancellationToken, Task<DashboardResult>> handler,
        IReadOnlyList<string>? accept = null, long maxBytes = 5_000_000) => new UiUpload(id, label, handler, accept ?? [], maxBytes);
}

/// <summary>页面组件。由 <see cref="Ui"/> 创建。</summary>
public abstract class UiNode
{
    private protected UiNode() { }
}

public sealed class UiLayout : UiNode
{
    internal UiLayout(string kind, string? title, IEnumerable<UiNode?> children)
    {
        Kind = kind;
        Title = title;
        Children = children.OfType<UiNode>().ToArray();
    }

    /// <summary>column、row 或 card。</summary>
    public string Kind { get; }
    public string? Title { get; }
    public IReadOnlyList<UiNode> Children { get; }
}

public sealed class UiTabs : UiNode
{
    internal UiTabs(string id, IReadOnlyList<UiTab> tabs)
    {
        Id = id;
        Tabs = tabs;
    }

    public string Id { get; }
    public IReadOnlyList<UiTab> Tabs { get; }
}

public sealed class UiTab
{
    internal UiTab(string id, string title, IEnumerable<UiNode?> children)
    {
        Id = id;
        Title = title;
        Children = children.OfType<UiNode>().ToArray();
    }

    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<UiNode> Children { get; }
}

public sealed class UiDivider : UiNode;

public sealed class UiText : UiNode
{
    internal UiText(string text, bool markdown)
    {
        Text = text;
        Markdown = markdown;
    }

    public string Text { get; }
    public bool Markdown { get; }
}

public sealed class UiNotice : UiNode
{
    internal UiNotice(string text, string tone)
    {
        Text = text;
        Tone = tone;
    }

    public string Text { get; }
    public string Tone { get; }
}

public sealed class UiMetric : UiNode
{
    internal UiMetric(string label, string value, string? hint)
    {
        Label = label;
        Value = value;
        Hint = hint;
    }

    public string Label { get; }
    public string Value { get; }
    public string? Hint { get; }
}

public sealed class UiImage : UiNode
{
    internal UiImage(string fileId, string? alt)
    {
        FileId = fileId;
        Alt = alt;
    }

    public string FileId { get; }
    public string? Alt { get; }
}

public sealed class UiCode : UiNode
{
    internal UiCode(string text) => Text = text;

    public string Text { get; }
}

public sealed class UiLink : UiNode
{
    internal UiLink(string text, string? page, string? url)
    {
        Text = text;
        Page = page;
        Url = url;
    }

    public string Text { get; }
    public string? Page { get; }
    public string? Url { get; }
}

public sealed class UiEmpty : UiNode
{
    internal UiEmpty(string text) => Text = text;

    public string Text { get; }
}

/// <summary>
/// 卡片：图片网格和列表里的一项。构造函数只接收 ID 和标题，其余内容用属性设置：
/// <c>new UiCard(meme.Id, meme.Caption) { Image = meme.Id, Tags = meme.Tags }</c>。
/// </summary>
public sealed record UiCard
{
    public UiCard(string id, string title)
    {
        Id = id;
        Title = title;
    }

    /// <summary>条目的 key，操作按它找回条目。</summary>
    public string Id { get; init; }

    public string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>图片的文件 ID，通过 AddPage 的 files 读取。</summary>
    public string? Image { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>次要信息，例如日期或大小。</summary>
    public string? Meta { get; init; }
}

/// <summary>数据组件里一行（或一张卡片）的显示内容和可用的操作。由 SDK 生成，插件只读取。</summary>
public sealed record UiRow
{
    internal UiRow(string key, IReadOnlyList<string> cells, UiCard? card, IReadOnlyList<string> actions)
    {
        Key = key;
        Cells = cells;
        Card = card;
        Actions = actions;
    }

    public string Key { get; }

    /// <summary>表格各列的文本；图片网格和列表为空。</summary>
    public IReadOnlyList<string> Cells { get; }

    /// <summary>图片网格和列表的卡片；表格为 null。</summary>
    public UiCard? Card { get; }

    /// <summary>这一行可用的操作 ID。</summary>
    public IReadOnlyList<string> Actions { get; }
}

/// <summary>数据组件声明的一个操作。由 SDK 根据 <c>.Action(...)</c> 生成，插件只读取。</summary>
public sealed record UiActionInfo
{
    internal UiActionInfo(string id, string label, string? confirm, bool danger)
    {
        Id = id;
        Label = label;
        Confirm = confirm;
        Danger = danger;
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>执行前的确认提示；为 null 时不确认。</summary>
    public string? Confirm { get; }

    public bool Danger { get; }
}

/// <summary>表格、图片网格和列表的共同部分。</summary>
public abstract class UiCollection : UiNode
{
    private protected UiCollection() { }

    /// <summary>table、gallery 或 list。</summary>
    public abstract string Kind { get; }

    /// <summary>表格的列标题。</summary>
    public abstract IReadOnlyList<string> Columns { get; }

    public abstract IReadOnlyList<UiActionInfo> Actions { get; }

    /// <summary>按当前数据生成每一行。</summary>
    public abstract IReadOnlyList<UiRow> Rows();

    /// <summary>由宿主调用：对 <paramref name="key"/> 对应的条目执行操作；条目或操作不存在、或操作不适用于该条目时返回 null。</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract Task<DashboardResult>? Invoke(string actionId, string key, DashboardEvent e, CancellationToken cancellationToken);
}

public abstract class UiCollection<T> : UiCollection
{
    private readonly IReadOnlyList<T> _items;
    private readonly List<(UiActionInfo Info, Func<T, DashboardEvent, CancellationToken, Task<DashboardResult>> Handler, Func<T, bool>? When)> _actions = [];

    private protected UiCollection(IEnumerable<T> items) => _items = items.ToArray();

    public override IReadOnlyList<UiActionInfo> Actions => _actions.Select(action => action.Info).ToArray();

    private protected IReadOnlyList<T> Items => _items;

    private protected abstract string KeyOf(T item);

    private protected abstract UiRow Describe(T item, IReadOnlyList<string> actions);

    private protected void AddAction(string id, string label, Func<T, DashboardEvent, CancellationToken, Task<DashboardResult>> handler,
        string? confirm, bool danger, Func<T, bool>? when)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(handler);
        if (_actions.Any(action => string.Equals(action.Info.Id, id, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"操作 {id} 已经声明过。", nameof(id));
        _actions.Add((new UiActionInfo(id, label, confirm, danger), handler, when));
    }

    public override IReadOnlyList<UiRow> Rows() =>
        _items.Select(item => Describe(item, _actions.Where(action => action.When?.Invoke(item) != false)
            .Select(action => action.Info.Id).ToArray())).ToArray();

    public override Task<DashboardResult>? Invoke(string actionId, string key, DashboardEvent e, CancellationToken cancellationToken)
    {
        var action = _actions.FirstOrDefault(candidate => string.Equals(candidate.Info.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (action.Handler is null) return null;
        foreach (var item in _items)
        {
            if (!string.Equals(KeyOf(item), key, StringComparison.Ordinal)) continue;
            return action.When?.Invoke(item) == false ? null : action.Handler(item, e, cancellationToken);
        }
        return null;
    }
}

public sealed class UiTable<T> : UiCollection<T>
{
    private readonly Func<T, string> _key;
    private readonly List<(string Title, Func<T, string?> Value)> _columns = [];

    internal UiTable(IEnumerable<T> items, Func<T, string> key) : base(items) => _key = key;

    public override string Kind => "table";
    public override IReadOnlyList<string> Columns => _columns.Select(column => column.Title).ToArray();

    /// <summary>声明一列。</summary>
    public UiTable<T> Column(string title, Func<T, string?> value)
    {
        _columns.Add((title, value));
        return this;
    }

    /// <summary>每行的操作；<paramref name="when"/> 返回 false 的行不显示这个操作。</summary>
    public UiTable<T> Action(string id, string label, Func<T, DashboardEvent, CancellationToken, Task<DashboardResult>> handler,
        string? confirm = null, bool danger = false, Func<T, bool>? when = null)
    {
        AddAction(id, label, handler, confirm, danger, when);
        return this;
    }

    private protected override string KeyOf(T item) => _key(item);

    private protected override UiRow Describe(T item, IReadOnlyList<string> actions) =>
        new(_key(item), _columns.Select(column => column.Value(item) ?? "").ToArray(), null, actions);
}

public sealed class UiCards<T> : UiCollection<T>
{
    private readonly Func<T, UiCard> _card;

    internal UiCards(string kind, IEnumerable<T> items, Func<T, UiCard> card) : base(items)
    {
        Kind = kind;
        _card = card;
    }

    public override string Kind { get; }
    public override IReadOnlyList<string> Columns => [];

    /// <summary>每张卡片的操作；<paramref name="when"/> 返回 false 的卡片不显示这个操作。</summary>
    public UiCards<T> Action(string id, string label, Func<T, DashboardEvent, CancellationToken, Task<DashboardResult>> handler,
        string? confirm = null, bool danger = false, Func<T, bool>? when = null)
    {
        AddAction(id, label, handler, confirm, danger, when);
        return this;
    }

    private protected override string KeyOf(T item) => _card(item).Id;

    private protected override UiRow Describe(T item, IReadOnlyList<string> actions)
    {
        var card = _card(item);
        return new UiRow(card.Id, [], card, actions);
    }
}

public sealed record UiOption(string Value, string Label);

public sealed class UiFilter
{
    internal UiFilter(string key, string label, string kind, IReadOnlyList<UiOption> options)
    {
        Key = key;
        Label = label;
        Kind = kind;
        Options = options;
    }

    public string Key { get; }
    public string Label { get; }

    /// <summary>input、select 或 switch。</summary>
    public string Kind { get; }
    public IReadOnlyList<UiOption> Options { get; }
}

public sealed class UiFilters : UiNode
{
    internal UiFilters(IReadOnlyList<UiFilter> filters) => Filters = filters;

    public IReadOnlyList<UiFilter> Filters { get; }
}

public sealed class UiPager : UiNode
{
    internal UiPager(string id, int? total, int pageSize, bool? hasMore)
    {
        Id = id;
        Total = total;
        PageSize = pageSize;
        HasMore = hasMore;
    }

    public string Id { get; }
    public int? Total { get; }
    public int PageSize { get; }
    public bool? HasMore { get; }
}

public sealed class UiButton : UiNode
{
    private readonly Func<DashboardEvent, CancellationToken, Task<DashboardResult>> _handler;

    internal UiButton(string id, string label, Func<DashboardEvent, CancellationToken, Task<DashboardResult>> handler, string? confirm, bool danger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Label = label;
        Confirm = confirm;
        Danger = danger;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public string Id { get; }
    public string Label { get; }
    public string? Confirm { get; }
    public bool Danger { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<DashboardResult> Invoke(DashboardEvent e, CancellationToken cancellationToken) => _handler(e, cancellationToken);
}

public sealed class UiUpload : UiNode
{
    private readonly Func<DashboardUploadFile, DashboardEvent, CancellationToken, Task<DashboardResult>> _handler;

    internal UiUpload(string id, string label, Func<DashboardUploadFile, DashboardEvent, CancellationToken, Task<DashboardResult>> handler,
        IReadOnlyList<string> accept, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Label = label;
        Accept = accept;
        MaxBytes = maxBytes;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public string Id { get; }
    public string Label { get; }

    /// <summary>允许的 MIME 类型，可以用通配，例如 image/*；为空表示不限。</summary>
    public IReadOnlyList<string> Accept { get; }
    public long MaxBytes { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<DashboardResult> Invoke(DashboardUploadFile file, DashboardEvent e, CancellationToken cancellationToken) =>
        _handler(file, e, cancellationToken);
}
