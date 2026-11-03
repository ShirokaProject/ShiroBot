using System.Text.Json.Nodes;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Plugins.Dashboard;

/// <summary>
/// 把组件树写成给 Dashboard 的 JSON，并记下可交互的元素：按钮、表单、上传、表格/网格/列表。
/// 没有显式 ID 的数据组件按在树里的位置编号，同样的状态渲染出同样的编号，事件据此找回元素。
/// </summary>
internal sealed class UiTreeWriter
{
    private const int MaxRows = 500;
    private readonly Dictionary<string, UiNode> _elements = new(StringComparer.Ordinal);

    private UiTreeWriter() { }

    public IReadOnlyDictionary<string, UiNode> Elements => _elements;

    public static (JsonObject View, UiTreeWriter Index) Write(UiNode root)
    {
        var writer = new UiTreeWriter();
        return (writer.Node(root, "n"), writer);
    }

    private JsonObject Node(UiNode node, string path) => node switch
    {
        UiLayout layout => new JsonObject
        {
            ["type"] = layout.Kind,
            ["title"] = layout.Title,
            ["children"] = Children(layout.Children, path)
        },
        UiTabs tabs => Register(tabs.Id, tabs, new JsonObject
        {
            ["type"] = "tabs",
            ["id"] = tabs.Id,
            ["tabs"] = new JsonArray([.. tabs.Tabs.Select((tab, index) => (JsonNode)new JsonObject
            {
                ["id"] = tab.Id,
                ["title"] = tab.Title,
                ["children"] = Children(tab.Children, $"{path}.{index}")
            })])
        }),
        UiDivider => new JsonObject { ["type"] = "divider" },
        UiText text => new JsonObject { ["type"] = "text", ["text"] = text.Text, ["markdown"] = text.Markdown },
        UiNotice notice => new JsonObject
        {
            ["type"] = "notice", ["text"] = notice.Text,
            ["tone"] = notice.Tone is "success" or "warning" or "danger" ? notice.Tone : "info"
        },
        UiMetric metric => new JsonObject { ["type"] = "metric", ["label"] = metric.Label, ["value"] = metric.Value, ["hint"] = metric.Hint },
        UiImage image => new JsonObject { ["type"] = "image", ["file"] = image.FileId, ["alt"] = image.Alt },
        UiCode code => new JsonObject { ["type"] = "code", ["text"] = code.Text },
        UiLink link => new JsonObject
        {
            ["type"] = "link", ["text"] = link.Text, ["page"] = link.Page,
            // 外部链接只允许 http(s)，避免 javascript: 之类的地址。
            ["url"] = Uri.TryCreate(link.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" ? url.ToString() : null
        },
        UiEmpty empty => new JsonObject { ["type"] = "empty", ["text"] = empty.Text },
        UiCollection collection => Collection(collection, path),
        UiFilters filters => new JsonObject
        {
            ["type"] = "filters",
            ["filters"] = new JsonArray([.. filters.Filters.Select(filter => (JsonNode)new JsonObject
            {
                ["key"] = filter.Key,
                ["label"] = filter.Label,
                ["kind"] = filter.Kind,
                ["options"] = Options(filter.Options)
            })])
        },
        UiPager pager => new JsonObject
        {
            ["type"] = "pager", ["id"] = pager.Id, ["total"] = pager.Total, ["page_size"] = pager.PageSize, ["has_more"] = pager.HasMore
        },
        UiButton button => Register(button.Id, button, new JsonObject
        {
            ["type"] = "button", ["id"] = button.Id, ["label"] = button.Label, ["confirm"] = button.Confirm, ["danger"] = button.Danger
        }),
        UiForm form => Register(form.Id, form, Form(form)),
        UiUpload upload => Register(upload.Id, upload, new JsonObject
        {
            ["type"] = "upload", ["id"] = upload.Id, ["label"] = upload.Label,
            ["accept"] = new JsonArray([.. upload.Accept.Select(type => (JsonNode)type)]),
            ["max_bytes"] = Math.Min(upload.MaxBytes, PluginDashboard.MaxUploadBytes)
        }),
        _ => throw new NotSupportedException($"未知的页面组件: {node.GetType().Name}")
    };

    private JsonArray Children(IReadOnlyList<UiNode> children, string path) =>
        new([.. children.Select((child, index) => (JsonNode)Node(child, $"{path}.{index}"))]);

    private JsonObject Collection(UiCollection collection, string path)
    {
        var rows = collection.Rows();
        if (rows.Count > MaxRows)
            throw new InvalidOperationException($"一个数据组件最多显示 {MaxRows} 行，请用 Ui.Pager 分页。");
        return Register(path, collection, new JsonObject
        {
            ["type"] = collection.Kind,
            ["id"] = path,
            ["columns"] = new JsonArray([.. collection.Columns.Select(column => (JsonNode)column)]),
            ["actions"] = new JsonArray([.. collection.Actions.Select(action => (JsonNode)new JsonObject
            {
                ["id"] = action.Id, ["label"] = action.Label, ["confirm"] = action.Confirm, ["danger"] = action.Danger
            })]),
            ["rows"] = new JsonArray([.. rows.Select(row => (JsonNode)new JsonObject
            {
                ["key"] = row.Key,
                ["cells"] = new JsonArray([.. row.Cells.Select(cell => (JsonNode)cell)]),
                ["card"] = row.Card is not { } card ? null : new JsonObject
                {
                    ["title"] = card.Title,
                    ["description"] = card.Description,
                    ["image"] = card.Image,
                    ["tags"] = new JsonArray([.. (card.Tags ?? []).Select(tag => (JsonNode)tag)]),
                    ["meta"] = card.Meta
                },
                ["actions"] = new JsonArray([.. row.Actions.Select(action => (JsonNode)action)])
            })])
        });
    }

    private static JsonObject Form(UiForm form) => new()
    {
        ["type"] = "form",
        ["id"] = form.Id,
        ["dialog"] = form.Dialog,
        ["submit_label"] = form.HasSubmit ? form.SubmitLabel : null,
        ["fields"] = new JsonArray([.. form.Fields.Select(field => (JsonNode)new JsonObject
        {
            ["key"] = field.Key,
            ["label"] = field.Label,
            ["kind"] = FieldKind(field.Kind),
            ["required"] = field.Required,
            ["max_length"] = field.MaxLength,
            ["pattern"] = field.Pattern,
            ["min"] = field.Min,
            ["max"] = field.Max,
            ["integer"] = field.Integer,
            ["options"] = Options(field.Options),
            ["value"] = field.Kind == UiFieldKind.Secret ? null : field.Value?.DeepClone(),
            ["description"] = field.Description
        })])
    };

    public static string FieldKind(UiFieldKind kind) => kind switch
    {
        UiFieldKind.TextArea => "textarea",
        UiFieldKind.Secret => "secret",
        UiFieldKind.Number => "number",
        UiFieldKind.Switch => "switch",
        UiFieldKind.Select => "select",
        UiFieldKind.MultiSelect => "multiselect",
        UiFieldKind.User => "user",
        UiFieldKind.Users => "users",
        UiFieldKind.Group => "group",
        UiFieldKind.Groups => "groups",
        _ => "text"
    };

    private static JsonArray Options(IReadOnlyList<UiOption> options) =>
        new([.. options.Select(option => (JsonNode)new JsonObject { ["value"] = option.Value, ["label"] = option.Label })]);

    private JsonObject Register(string id, UiNode node, JsonObject json)
    {
        if (!_elements.TryAdd(id, node)) throw new InvalidOperationException($"页面里有两个 ID 都是 {id} 的组件。");
        return json;
    }
}
