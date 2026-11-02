using System.Text.Json.Nodes;

namespace ShiroBot.SDK.Plugin;

/// <summary>
/// 插件之间按名称提供和调用的服务。参数和返回值都是 JSON，双方不共享程序集和对象，
/// 任何一方都可以单独卸载和更新。
/// </summary>
public interface IPluginServices
{
    /// <summary>
    /// 按名称提供服务（例如 "weather.query"），参数和返回值都是 JSON，调用方不需要引用任何共享程序集。
    /// 服务随插件卸载自动注销，提供方可以随时卸载和重新加载，调用方无需跟着卸载。
    /// 同一名称同时只能由一个插件提供；返回的对象 Dispose 后提前注销。
    /// </summary>
    IDisposable Provide(string name, Func<JsonNode?, CancellationToken, Task<JsonNode?>> handler) =>
        throw new NotSupportedException("This host does not support named plugin services.");

    /// <summary>
    /// 调用按名称提供的服务。参数和返回值在插件之间复制传递，双方不共享对象。
    /// 没有提供方或提供方正在卸载时抛出 <see cref="PluginServiceUnavailableException"/>；
    /// 提供方处理失败时抛出 <see cref="PluginServiceException"/>。
    /// </summary>
    Task<JsonNode?> CallAsync(string name, JsonNode? arguments = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This host does not support named plugin services.");

    /// <summary>当前是否有插件提供该名称的服务。</summary>
    bool IsAvailable(string name) => false;

    /// <summary>
    /// 提供服务并附带描述信息（例如工具说明和参数格式），其他插件可以用 <see cref="Find"/> 按名称前缀发现它。
    /// 描述在注册时复制保存；要更新描述，注销后重新提供。
    /// </summary>
    IDisposable Provide(string name, Func<JsonNode?, CancellationToken, Task<JsonNode?>> handler, JsonNode? metadata) =>
        metadata is null ? Provide(name, handler) : throw new NotSupportedException("This host does not support service metadata.");

    /// <summary>
    /// 列出名称以 <paramref name="prefix"/> 开头、当前可调用的服务（例如 "chat.tool."），按名称排序。
    /// 描述是副本；列出不会让调用方依赖提供方，提供方仍可随时卸载。
    /// </summary>
    IReadOnlyList<PluginServiceInfo> Find(string prefix) => [];

    /// <summary>以 UTF-8 JSON 字节提供服务，供强类型封装使用；空字节表示 JSON null。</summary>
    IDisposable ProvideUtf8(string name, PluginUtf8Handler handler, ReadOnlyMemory<byte> metadata = default) =>
        throw new NotSupportedException("This host does not support named plugin services.");

    /// <summary>以 UTF-8 JSON 字节调用服务，供强类型封装使用；空字节表示 JSON null。</summary>
    Task<ReadOnlyMemory<byte>> CallUtf8Async(string name, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This host does not support named plugin services.");
}

/// <summary>以 UTF-8 JSON 字节处理一次服务调用。</summary>
public delegate Task<ReadOnlyMemory<byte>> PluginUtf8Handler(ReadOnlyMemory<byte> request, CancellationToken cancellationToken);

/// <summary>以 UTF-8 JSON 字节处理一条事件。</summary>
public delegate Task PluginUtf8EventHandler(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

/// <summary>按名称提供的服务：名称、提供方插件 ID 和注册时附带的描述。</summary>
public sealed record PluginServiceInfo(string Name, string ProviderId, JsonNode? Metadata)
{
    /// <summary>描述的 UTF-8 JSON 字节；没有描述时为空。</summary>
    public ReadOnlyMemory<byte> MetadataUtf8 { get; init; }

    /// <summary>按契约类型读出描述；没有描述时为 <c>default</c>。</summary>
    public T? GetMetadata<T>() => PluginJson.Deserialize<T>(MetadataUtf8.Span);
}
