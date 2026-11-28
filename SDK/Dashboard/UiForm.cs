using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Dashboard;

/// <summary>表单字段的种类。</summary>
public enum UiFieldKind
{
    Text = 0,
    TextArea = 1,
    Secret = 2,
    Number = 3,
    Switch = 4,
    Select = 5,
    MultiSelect = 6,

    /// <summary>一个好友，值为 实例ID:用户ID。</summary>
    User = 7,

    /// <summary>多个好友。</summary>
    Users = 8,

    /// <summary>一个群，值为群号。</summary>
    Group = 9,

    /// <summary>多个群。</summary>
    Groups = 10
}

/// <summary>
///     指定 <see cref="Ui.Form{T}" /> 里这个字段用什么控件，例如群选择器或多行文本；
///     不标时按类型推断：字符串是文本，数字是数字，布尔是开关，枚举是单选。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class UiFieldAttribute : Attribute
{
    public UiFieldKind Kind { get; set; }
}

/// <summary>表单的一个字段。</summary>
public sealed class UiFormField
{
    internal UiFormField(string key, string label, UiFieldKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Label = label;
        Kind = kind;
    }

    public string Key { get; }
    public string Label { get; }
    public UiFieldKind Kind { get; }
    public bool Required { get; internal set; }
    public int? MaxLength { get; internal set; }

    /// <summary>文本必须匹配的正则表达式。</summary>
    public string? Pattern { get; internal set; }

    public double? Min { get; internal set; }
    public double? Max { get; internal set; }

    /// <summary>数字只接受整数。</summary>
    public bool Integer { get; internal set; }

    public IReadOnlyList<UiOption> Options { get; internal set; } = [];

    /// <summary>字段的初始值。</summary>
    public JsonNode? Value { get; internal set; }

    public string? Description { get; internal set; }
}

/// <summary>
///     表单。提交时宿主先检查字段：只接受声明过的字段，类型、必填、长度、范围和选项都符合才调用处理函数。
/// </summary>
public class UiForm : UiNode
{
    private readonly List<UiFormField> _fields = [];
    private Func<DashboardForm, DashboardEvent, CancellationToken, Task<DashboardResult>>? _submit;

    internal UiForm(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }
    public IReadOnlyList<UiFormField> Fields => _fields;
    public string SubmitLabel { get; private protected set; } = "保存";

    /// <summary>为 true 时不显示在页面里，只在处理结果 <see cref="DashboardResult.OpenForm(string, JsonObject?)" /> 时以对话框打开。</summary>
    public bool Dialog { get; private set; }

    public bool HasSubmit => _submit is not null;

    public UiForm Text(string key, string label, bool required = false, int? maxLength = null, string? pattern = null,
        string? value = null)
    {
        return Add(new UiFormField(key, label, UiFieldKind.Text)
            { Required = required, MaxLength = maxLength, Pattern = pattern, Value = value });
    }

    public UiForm TextArea(string key, string label, bool required = false, int? maxLength = null, string? value = null)
    {
        return Add(new UiFormField(key, label, UiFieldKind.TextArea)
            { Required = required, MaxLength = maxLength, Value = value });
    }

    public UiForm Secret(string key, string label, bool required = false)
    {
        return Add(new UiFormField(key, label, UiFieldKind.Secret) { Required = required });
    }

    public UiForm Number(string key, string label, double? min = null, double? max = null, bool integer = false,
        bool required = false, double? value = null)
    {
        return Add(new UiFormField(key, label, UiFieldKind.Number)
        {
            Min = min, Max = max, Integer = integer, Required = required,
            Value = value is null ? null : JsonValue.Create(value.Value)
        });
    }

    public UiForm Switch(string key, string label, bool value = false)
    {
        return Add(new UiFormField(key, label, UiFieldKind.Switch) { Value = value });
    }

    public UiForm Select(string key, string label, IEnumerable<(string Value, string Label)> options,
        bool required = false, string? value = null)
    {
        return Add(new UiFormField(key, label, UiFieldKind.Select)
        {
            Options = ToOptions(options), Required = required, Value = value
        });
    }

    public UiForm MultiSelect(string key, string label, IEnumerable<(string Value, string Label)> options,
        bool required = false)
    {
        return Add(new UiFormField(key, label, UiFieldKind.MultiSelect)
            { Options = ToOptions(options), Required = required });
    }

    public UiForm Users(string key, string label, bool required = false)
    {
        return Add(new UiFormField(key, label, UiFieldKind.Users) { Required = required });
    }

    public UiForm Groups(string key, string label, bool required = false)
    {
        return Add(new UiFormField(key, label, UiFieldKind.Groups) { Required = required });
    }

    /// <summary>提交按钮和处理函数。</summary>
    public UiForm Submit(string label,
        Func<DashboardForm, DashboardEvent, CancellationToken, Task<DashboardResult>> handler)
    {
        SubmitLabel = label;
        _submit = handler ?? throw new ArgumentNullException(nameof(handler));
        return this;
    }

    /// <summary>不显示在页面里，只在处理结果 OpenForm 时以对话框打开，例如“编辑”。</summary>
    public UiForm AsDialog()
    {
        Dialog = true;
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<DashboardResult> Invoke(DashboardForm form, DashboardEvent e, CancellationToken cancellationToken)
    {
        return (_submit ?? throw new InvalidOperationException($"表单 {Id} 没有提交处理函数。"))(form, e, cancellationToken);
    }

    private protected UiForm Add(UiFormField field)
    {
        if (_fields.Any(existing => string.Equals(existing.Key, field.Key, StringComparison.Ordinal)))
            throw new ArgumentException($"字段 {field.Key} 已经声明过。");
        _fields.Add(field);
        return this;
    }

    private protected void SetSubmit(string label,
        Func<DashboardForm, DashboardEvent, CancellationToken, Task<DashboardResult>> handler)
    {
        SubmitLabel = label;
        _submit = handler;
    }

    private static UiOption[] ToOptions(IEnumerable<(string Value, string Label)> options)
    {
        return [.. options.Select(option => new UiOption(option.Value, option.Label))];
    }
}

/// <summary>
///     按 record 生成的表单：字段取自它生成的 JSON Schema（字段类型、默认值、<c>[Description]</c> 作为标题、
///     <c>[Range]</c>、<c>[MaxLength]</c>、<c>[UiField]</c>、必填），提交时把值读成 record 交给处理函数。
/// </summary>
public sealed class UiForm<T> : UiForm
{
    internal UiForm(string id) : base(id)
    {
        var schema = PluginJsonSchema.For<T>();
        var required = (schema["required"] as JsonArray)?.Select(node => node?.GetValue<string>()).ToHashSet() ?? [];
        foreach (var (key, node) in schema["properties"] as JsonObject ?? [])
        {
            if (node is not JsonObject property) continue;
            Add(FieldFrom(key, property, required.Contains(key)));
        }
    }

    /// <summary>提交按钮和处理函数，收到读成 <typeparamref name="T" /> 的值。</summary>
    public UiForm<T> Submit(string label, Func<T, DashboardEvent, CancellationToken, Task<DashboardResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetSubmit(label, (form, e, ct) =>
            handler(PluginJson.Deserialize<T>(Encoding.UTF8.GetBytes(form.Values.ToJsonString()))!, e, ct));
        return this;
    }

    /// <summary>不显示在页面里，只在处理结果 OpenForm 时以对话框打开。</summary>
    public new UiForm<T> AsDialog()
    {
        base.AsDialog();
        return this;
    }

    /// <summary>用一个值预填表单。</summary>
    public UiForm<T> Value(T value)
    {
        if (PluginJson.ToNode(value) is not JsonObject values)
        {
            return this;
        }

        foreach (var field in Fields)
            if (values[field.Key] is { } node)
                field.Value = node.DeepClone();
        return this;
    }

    private static UiFormField FieldFrom(string key, JsonObject property, bool required)
    {
        var label = (string?)property["description"] ?? key;
        var type = (string?)property["type"];
        var hint = (string?)property["x-ui"];
        var options = (property["enum"] ?? property["items"]?["enum"]) is JsonArray values
            ? values.Select(value => new UiOption(value!.GetValue<string>(), value.GetValue<string>())).ToArray()
            : [];
        var kind = hint switch
        {
            "textarea" => UiFieldKind.TextArea,
            "secret" => UiFieldKind.Secret,
            "user" => UiFieldKind.User,
            "users" => UiFieldKind.Users,
            "group" => UiFieldKind.Group,
            "groups" => UiFieldKind.Groups,
            _ => type switch
            {
                "integer" or "number" => UiFieldKind.Number,
                "boolean" => UiFieldKind.Switch,
                "array" when options.Length > 0 => UiFieldKind.MultiSelect,
                "string" when options.Length > 0 => UiFieldKind.Select,
                "string" => UiFieldKind.Text,
                _ => throw new NotSupportedException($"字段 {key} 的类型不能放进表单；可以用 Ui.Form(id) 逐个声明字段。")
            }
        };
        return new UiFormField(key, label, kind)
        {
            Required = required,
            Options = options,
            Integer = type == "integer",
            Min = (double?)property["minimum"],
            Max = (double?)property["maximum"],
            MaxLength = (int?)property["maxLength"],
            Value = property["default"]?.DeepClone()
        };
    }
}