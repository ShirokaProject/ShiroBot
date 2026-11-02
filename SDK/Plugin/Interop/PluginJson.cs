using System.Buffers;
using System.ComponentModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShiroBot.SDK.Plugin;

/// <summary>
/// 一个契约类型的 JSON 读写代码和 JSON Schema，由 SDK 在编译时为契约类型生成。
/// 生成的代码留在插件自己的程序集里，不经过宿主的反射序列化，插件卸载时不会被拉住。
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class PluginJsonType<T>
{
    public abstract void Write(Utf8JsonWriter writer, T value);

    public abstract T Read(ref Utf8JsonReader reader);

    /// <summary>JSON Schema 文本。</summary>
    public abstract string Schema { get; }
}

/// <summary>契约类型 <typeparamref name="T"/> 的读写代码，由生成的模块初始化器登记。</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PluginJson<T>
{
    public static PluginJsonType<T>? Type { get; internal set; }
}

/// <summary>契约类型与 JSON 之间的转换。只支持 SDK 生成了读写代码的契约类型。</summary>
public static class PluginJson
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>由生成的模块初始化器调用，登记契约类型的读写代码。</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register<T>(PluginJsonType<T> type) => PluginJson<T>.Type = type;

    /// <summary>把契约类型序列化成 UTF-8 JSON；<c>null</c> 得到空字节。</summary>
    public static byte[] Serialize<T>(T value) => Write(TypeOf<T>(), value);

    /// <summary>从 UTF-8 JSON 读出契约类型；空字节和 JSON null 得到 <c>default</c>。</summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> json) => Read(TypeOf<T>(), json);

    /// <summary>把契约类型转换成 <see cref="JsonNode"/>。</summary>
    public static JsonNode? ToNode<T>(T value)
    {
        var json = Serialize(value);
        return json.Length == 0 ? null : JsonNode.Parse(json);
    }

    /// <summary>把 <see cref="JsonNode"/> 读成契约类型，例如收到的工具参数。</summary>
    public static T? As<T>(this JsonNode? node) =>
        node is null ? default : Deserialize<T>(Encoding.UTF8.GetBytes(node.ToJsonString()));

    internal static PluginJsonType<T> TypeOf<T>() =>
        PluginJson<T>.Type ?? throw new InvalidOperationException(
            $"{typeof(T).FullName} 没有生成 JSON 读写代码。把它用在 [PluginContract] 的服务或事件里，或给它标上 [PluginJson]。");

    internal static byte[] Write<T>(PluginJsonType<T> type, T value)
    {
        if (value is null) return [];
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) type.Write(writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    internal static T? Read<T>(PluginJsonType<T> type, ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty) return default;
        var reader = new Utf8JsonReader(json, ReaderOptions);
        if (!reader.Read() || reader.TokenType == JsonTokenType.Null) return default;
        return type.Read(ref reader);
    }

    internal static byte[] FromNode(JsonNode? node) => node is null ? [] : Encoding.UTF8.GetBytes(node.ToJsonString());
}

/// <summary>生成的读写代码使用的辅助方法。</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PluginJsonIO
{
    /// <summary>当前是对象开始时返回 true；JSON null 返回 false；其他抛出异常。</summary>
    public static bool StartObject(ref Utf8JsonReader reader, string type)
    {
        if (reader.TokenType == JsonTokenType.Null) return false;
        if (reader.TokenType != JsonTokenType.StartObject) throw Unexpected(ref reader, type, "object");
        return true;
    }

    /// <summary>当前是数组开始时返回 true；JSON null 返回 false；其他抛出异常。</summary>
    public static bool StartArray(ref Utf8JsonReader reader, string field)
    {
        if (reader.TokenType == JsonTokenType.Null) return false;
        if (reader.TokenType != JsonTokenType.StartArray) throw Unexpected(ref reader, field, "array");
        return true;
    }

    public static void Expect(ref Utf8JsonReader reader, JsonTokenType token, string field)
    {
        if (reader.TokenType != token &&
            !(token == JsonTokenType.True && reader.TokenType == JsonTokenType.False))
            throw Unexpected(ref reader, field, token switch
            {
                JsonTokenType.Number => "number",
                JsonTokenType.String => "string",
                JsonTokenType.True => "boolean",
                _ => token.ToString()
            });
    }

    public static string? ReadString(ref Utf8JsonReader reader, string field)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        Expect(ref reader, JsonTokenType.String, field);
        return reader.GetString();
    }

    /// <summary>按名称读取枚举：不区分大小写，忽略下划线；也接受数字。</summary>
    public static T ReadEnum<T>(ref Utf8JsonReader reader, string field) where T : struct, Enum
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
            return (T)Enum.ToObject(typeof(T), number);
        var text = ReadString(ref reader, field)?.Replace("_", "", StringComparison.Ordinal);
        if (text is not null && Enum.TryParse<T>(text, ignoreCase: true, out var value)) return value;
        throw new JsonException($"字段 {field} 的值 \"{text}\" 不是 {typeof(T).Name} 的成员。");
    }

    /// <summary>枚举按成员名的 snake_case 写出。</summary>
    public static void WriteEnum<T>(Utf8JsonWriter writer, T value) where T : struct, Enum =>
        writer.WriteStringValue(Enum.GetName(value) is { } name ? SnakeCase(name) : Convert.ToInt64(value).ToString());

    public static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch))
            {
                var previousLower = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextLower = i > 0 && i + 1 < name.Length && char.IsUpper(name[i - 1]) && char.IsLower(name[i + 1]);
                if (previousLower || nextLower) builder.Append('_');
                builder.Append(char.ToLowerInvariant(ch));
            }
            else builder.Append(ch);
        }
        return builder.ToString();
    }

    private static JsonException Unexpected(ref Utf8JsonReader reader, string field, string expected) =>
        new($"字段 {field} 应为 {expected}，实际是 {reader.TokenType}。");
}
