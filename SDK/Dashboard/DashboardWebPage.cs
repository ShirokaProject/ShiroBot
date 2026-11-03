using System.Reflection;
using System.Text.Json.Nodes;

namespace ShiroBot.SDK.Dashboard;

/// <summary>
/// 沙箱网页及其嵌入资源。入口为自包含 HTML，额外资源通过网页桥接读取。
/// 资源使用保留相对路径的 LogicalName，例如 dashboard/index.html。
/// </summary>
public sealed class DashboardWebPage
{
    public DashboardWebPage(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);
        Assembly = assembly;
        ResourcePrefix = resourcePrefix;
    }

    public Assembly Assembly { get; }
    public string ResourcePrefix { get; }
    public string EntryPoint { get; init; } = "index.html";
    public string? Icon { get; init; }
    public string? Description { get; init; }
    public int Order { get; init; } = 100;

    /// <summary>网页调用的处理函数；action 由插件解释并校验，参数和结果按 JSON 复制。</summary>
    public Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? HandleAsync { get; init; }
}
