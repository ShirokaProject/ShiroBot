using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShiroBot.Plugins.Services;

/// <summary>JsonNode 与宿主内部 UTF-8 字节之间的转换；空字节和 JSON null 都表示 null。</summary>
internal static class PluginJsonBytes
{
    public static JsonNode? ToNode(ReadOnlyMemory<byte> json)
    {
        return json.IsEmpty ? null : JsonNode.Parse(json.Span);
    }

    public static ReadOnlyMemory<byte> FromNode(JsonNode? node)
    {
        return node is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    /// <summary>是否是一个完整的 JSON 值；空字节表示 JSON null，也算有效。</summary>
    public static bool IsValid(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty) return true;
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read()) return false;
            reader.Skip();
            return !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }
}