using System.Text.Json.Nodes;

namespace ShiroBot.Plugins.Services;

/// <summary>JsonNode 与宿主内部 UTF-8 字节之间的转换；空字节和 JSON null 都表示 null。</summary>
internal static class PluginJsonBytes
{
    public static JsonNode? ToNode(ReadOnlyMemory<byte> json) => json.IsEmpty ? null : JsonNode.Parse(json.Span);

    public static ReadOnlyMemory<byte> FromNode(JsonNode? node) =>
        node is null ? ReadOnlyMemory<byte>.Empty : System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
}
