using System.Text.Json.Nodes;

namespace ShiroBot.SDK.Plugin;

/// <summary>
/// Plugin-scoped access to services exported by other plugins.
/// Typed services (<see cref="RegisterSingleton{TService}"/>) need a contract from a shared assembly loaded by the host;
/// named services (<see cref="Provide"/> / <see cref="CallAsync"/>) exchange JSON and need no shared assembly.
/// </summary>
public interface IPluginServices : IServiceProvider
{
    void RegisterSingleton<TService>(TService service)
        where TService : class;

    TService? GetService<TService>()
        where TService : class;

    TService GetRequiredService<TService>()
        where TService : class;

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
}
