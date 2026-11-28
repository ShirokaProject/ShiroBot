using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

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

    /// <summary>
    /// 当前网页的业务 HTTP 入口。请求包含页面内路径、查询、业务头和原始请求体；
    /// 不含宿主鉴权信息。可用 ReadFormAsync、ReadFromJsonAsync 及 Results.File/Stream/Json。
    /// 响应流在插件执行保护范围内发送，完成后释放。
    /// </summary>
    public Func<HttpRequest, CancellationToken, Task<IResult>>? RequestAsync { get; init; }

    /// <summary>
    /// 需要用户在 Dashboard 中确认后才发出的操作，值是确认提示。键是 <see cref="HandleAsync"/> 的 action 名，
    /// 或 HTTP 方法加页面内路径前缀，例如 <c>"DELETE /memes"</c>（匹配 /memes 和 /memes/…）。
    /// 确认框由 Dashboard 在网页之外显示，网页里的脚本无法跳过；适合删除、清空、覆盖等操作。
    /// </summary>
    public IReadOnlyDictionary<string, string> Confirmations { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// 启用 Trusted Types：浏览器拒绝把字符串直接写进 innerHTML、document.write 等位置，
    /// 网页显示的不可信内容即使带有 HTML 也不会变成脚本。Vue、Svelte 等框架的运行时会用到这些写法，确认网页兼容后再开启。
    /// </summary>
    public bool StrictDom { get; init; }

    /// <summary>单次业务请求体上限，默认 256 MiB，宿主硬上限 1 GiB。</summary>
    public long MaxRequestBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>单次业务响应体上限，默认 256 MiB，宿主硬上限 1 GiB。</summary>
    public long MaxResponseBytes { get; init; } = 256 * 1024 * 1024;
}
