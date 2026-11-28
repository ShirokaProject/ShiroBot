using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Plugins.Dashboard;

/// <summary>
///     按表单的字段声明检查提交的值：只保留声明过的字段，类型、必填、长度、范围和选项都要符合。
///     通过时得到整理好的值；不通过时按字段给出原因，不会调用插件。
/// </summary>
internal static class FormValidator
{
    private const int MaxItems = 200;
    private const int MaxText = 20_000;

    public static (JsonObject Values, Dictionary<string, string> Errors) Validate(UiForm form, JsonObject? input)
    {
        var values = new JsonObject();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in form.Fields)
        {
            var raw = input?[field.Key];
            var (value, error) = Check(field, raw is null || raw.GetValueKind() == JsonValueKind.Null ? null : raw);
            if (error is not null) errors[field.Key] = error;
            else if (value is not null) values[field.Key] = value;
        }

        return (values, errors);
    }

    private static (JsonNode? Value, string? Error) Check(UiFormField field, JsonNode? raw)
    {
        switch (field.Kind)
        {
            case UiFieldKind.Text or UiFieldKind.TextArea or UiFieldKind.Secret:
            {
                if (raw is not null && raw.GetValueKind() != JsonValueKind.String) return (null, "应为文本。");
                var text = raw?.GetValue<string>() ?? "";
                if (text.Length == 0) return field.Required ? (null, "必填。") : (null, null);
                if (text.Length > (field.MaxLength ?? MaxText))
                    return (null, $"不能超过 {field.MaxLength ?? MaxText} 个字符。");
                if (field.Pattern is { } pattern &&
                    !Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                    return (null, "格式不正确。");
                return (text, null);
            }
            case UiFieldKind.Number:
            {
                if (raw is null || (raw.GetValueKind() == JsonValueKind.String &&
                                    raw.GetValue<string>().Trim().Length == 0))
                    return field.Required ? (null, "必填。") : (null, null);
                double number;
                if (raw.GetValueKind() == JsonValueKind.Number)
                {
                    if (!double.TryParse(raw.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out number)) return (null, "应为数字。");
                }
                else if (raw.GetValueKind() != JsonValueKind.String ||
                         !double.TryParse(raw.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture,
                             out number))
                {
                    return (null, "应为数字。");
                }

                if (double.IsNaN(number) || double.IsInfinity(number)) return (null, "应为数字。");
                return field switch
                {
                    { Integer: true } when !double.IsInteger(number) => (null, "应为整数。"),
                    { Integer: true } when number is < long.MinValue or >= 9223372036854775808d => (null, "整数超出范围。"),
                    { Min: { } min } when number < min => (null, $"不能小于 {min}。"),
                    { Max: { } max } when number > max => (null, $"不能大于 {max}。"),
                    { Integer: true } => (JsonValue.Create((long)number), null),
                    _ => (JsonValue.Create(number), null)
                };
            }
            case UiFieldKind.Switch:
                if (raw is null) return (false, null);
                return raw.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                    ? (raw.GetValue<bool>(), null)
                    : (null, "应为开关。");
            case UiFieldKind.Select or UiFieldKind.User or UiFieldKind.Group:
            {
                if (raw is not null && raw.GetValueKind() != JsonValueKind.String) return (null, "选择无效。");
                var value = raw?.GetValue<string>() ?? "";
                if (value.Length == 0) return field.Required ? (null, "必填。") : (null, null);
                if (field.Kind == UiFieldKind.Select && field.Options.All(option => option.Value != value))
                    return (null, "不在可选范围内。");
                if (field.Kind != UiFieldKind.Select && !ValidContact(value, field.Kind == UiFieldKind.User))
                    return (null, "选择无效。");
                return (value, null);
            }
            case UiFieldKind.MultiSelect or UiFieldKind.Users or UiFieldKind.Groups:
            {
                if (raw is not null && raw.GetValueKind() != JsonValueKind.Array) return (null, "选择无效。");
                var items = new JsonArray();
                foreach (var item in raw?.AsArray() ?? [])
                {
                    if (item is null || item.GetValueKind() != JsonValueKind.String) return (null, "选择无效。");
                    var value = item.GetValue<string>();
                    var valid = field.Kind == UiFieldKind.MultiSelect
                        ? field.Options.Any(option => option.Value == value)
                        : ValidContact(value, field.Kind == UiFieldKind.Users);
                    if (!valid) return (null, "包含无效的选择。");
                    if (items.All(existing => existing!.GetValue<string>() != value)) items.Add(value);
                }

                return items.Count switch
                {
                    > MaxItems => (null, $"最多选择 {MaxItems} 项。"),
                    0 when field.Required => (null, "必填。"),
                    _ => (items, null)
                };
            }
            default:
                return (null, "不支持的字段。");
        }
    }

    /// <summary>好友为 实例ID:用户ID，群为群号；不允许空白和过长的值。</summary>
    private static bool ValidContact(string value, bool user)
    {
        return value.Length is > 0 and <= 200 && !value.Any(char.IsWhiteSpace) &&
               (!user || (value.IndexOf(':') is > 0 and var colon && colon < value.Length - 1));
    }
}