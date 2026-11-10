using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShiroBot.SDK.Plugin;

/// <summary>
///     按名称提供和调用的服务。一般在 <see cref="PluginContractAttribute" /> 契约类里声明为属性，由 SDK 生成实例；
///     运行时消息按 JSON 复制，双方不共享对象，任何一方都可以单独卸载和更新。
/// </summary>
public sealed class PluginService<TRequest, TResponse>
{
    private readonly PluginJsonType<TRequest> _request;
    private readonly PluginJsonType<TResponse> _response;

    public PluginService(string name, PluginJsonType<TRequest> request, PluginJsonType<TResponse> response)
    {
        PluginServiceNames.ValidateService(name);
        Name = name;
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _response = response ?? throw new ArgumentNullException(nameof(response));
    }

    public string Name { get; }

    /// <summary>提供服务，随插件卸载自动注销；返回的对象 Dispose 后提前注销。</summary>
    public IDisposable Provide(IBotContext context, Func<TRequest, CancellationToken, Task<TResponse>> handler,
        JsonNode? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        return context.Services.ProvideUtf8(Name,
            async (request, ct) => PluginJson.Write(_response,
                await handler(PluginJson.Read(_request, request.Span)!, ct).ConfigureAwait(false)),
            PluginJson.FromNode(metadata));
    }

    /// <summary>调用服务；没有插件提供或提供方正在卸载时返回 <c>null</c>。</summary>
    public Task<TResponse?> TryCallAsync(IBotContext context, TRequest request,
        CancellationToken cancellationToken = default)
    {
        return PluginServiceCalls.TryCallAsync(context, Name, _request, _response, request, cancellationToken);
    }

    /// <summary>调用服务；没有插件提供时抛出 <see cref="PluginServiceUnavailableException" />。</summary>
    public Task<TResponse> CallAsync(IBotContext context, TRequest request,
        CancellationToken cancellationToken = default)
    {
        return PluginServiceCalls.CallAsync(context, Name, _request, _response, request, cancellationToken);
    }

    public bool IsAvailable(IBotContext context)
    {
        return context.Services.IsAvailable(Name);
    }

    /// <summary>描述一次对这个服务的调用：<c>{ "service": 服务名, "arguments": 请求 }</c>，用于让其他插件稍后代为调用。</summary>
    public JsonObject ToCallJson(TRequest request)
    {
        return new JsonObject
            { ["service"] = Name, ["arguments"] = PluginJsonNode(PluginJson.Write(_request, request)) };
    }

    private static JsonNode? PluginJsonNode(byte[] json)
    {
        return json.Length == 0 ? null : JsonNode.Parse(json);
    }
}

/// <summary>不需要请求参数的服务。</summary>
public sealed class PluginService<TResponse>
{
    private readonly PluginJsonType<TResponse> _response;

    public PluginService(string name, PluginJsonType<TResponse> response)
    {
        PluginServiceNames.ValidateService(name);
        Name = name;
        _response = response ?? throw new ArgumentNullException(nameof(response));
    }

    public string Name { get; }

    public IDisposable Provide(IBotContext context, Func<CancellationToken, Task<TResponse>> handler,
        JsonNode? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        return context.Services.ProvideUtf8(Name,
            async (_, ct) => PluginJson.Write(_response, await handler(ct).ConfigureAwait(false)),
            PluginJson.FromNode(metadata));
    }

    public async Task<TResponse?> TryCallAsync(IBotContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await CallAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (PluginServiceUnavailableException)
        {
            return default;
        }
    }

    public async Task<TResponse> CallAsync(IBotContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = await context.Services.CallUtf8Async(Name, ReadOnlyMemory<byte>.Empty, cancellationToken)
            .ConfigureAwait(false);
        return PluginJson.Read(_response, response.Span)!;
    }

    public bool IsAvailable(IBotContext context)
    {
        return context.Services.IsAvailable(Name);
    }
}

/// <summary>
///     一类服务：名称以同一个前缀开头、请求和响应格式相同，例如 <c>weather.source.*</c>。
///     定义扩展点的插件声明它并用 <see cref="Find" /> 发现所有实现，其他插件各自 <see cref="Provide" /> 其中一个。
/// </summary>
public sealed class PluginServiceFamily<TRequest, TResponse, TMetadata>
{
    private readonly PluginJsonType<TMetadata> _metadata;
    private readonly PluginJsonType<TRequest> _request;
    private readonly PluginJsonType<TResponse> _response;

    /// <param name="pattern">以 <c>*</c> 结尾的名称模式，例如 <c>weather.source.*</c>。</param>
    /// <param name="request">JSON codec for service requests.</param>
    /// <param name="response">JSON codec for service responses.</param>
    /// <param name="metadata">JSON codec for service metadata.</param>
    public PluginServiceFamily(string pattern, PluginJsonType<TRequest> request, PluginJsonType<TResponse> response,
        PluginJsonType<TMetadata> metadata)
    {
        Prefix = PluginServiceNames.FamilyPrefix(pattern);
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>服务名前缀，例如 <c>weather.source.</c>。</summary>
    public string Prefix { get; }

    /// <summary>提供这一类中的一个服务，完整名称为 <see cref="Prefix" /> 加 <paramref name="name" />。</summary>
    public IDisposable Provide(IBotContext context, string name,
        Func<TRequest, CancellationToken, Task<TResponse>> handler, TMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        return context.Services.ProvideUtf8(Prefix + name,
            async (request, ct) => PluginJson.Write(_response,
                await handler(PluginJson.Read(_request, request.Span)!, ct).ConfigureAwait(false)),
            PluginJson.Write(_metadata, metadata));
    }

    /// <summary>按名称调用这一类中的一个服务；没有插件提供或提供方正在卸载时返回 <c>null</c>。</summary>
    public Task<TResponse?> TryCallAsync(IBotContext context, string name, TRequest request,
        CancellationToken cancellationToken = default)
    {
        return PluginServiceCalls.TryCallAsync(context, Prefix + name, _request, _response, request, cancellationToken);
    }

    /// <summary>按名称调用这一类中的一个服务；没有插件提供时抛出 <see cref="PluginServiceUnavailableException" />。</summary>
    public Task<TResponse> CallAsync(IBotContext context, string name, TRequest request,
        CancellationToken cancellationToken = default)
    {
        return PluginServiceCalls.CallAsync(context, Prefix + name, _request, _response, request, cancellationToken);
    }

    /// <summary>列出当前提供的所有服务，按名称排序。描述读不出来的服务仍会列出，描述为 <c>default</c>。</summary>
    public IReadOnlyList<PluginServiceEntry<TRequest, TResponse, TMetadata>> Find(IBotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            .. context.Services.Find(Prefix)
                .Select(info => new PluginServiceEntry<TRequest, TResponse, TMetadata>(
                    info.Name, info.Name[Prefix.Length..], info.ProviderId, ReadMetadata(info.MetadataUtf8), _request,
                    _response))
        ];
    }

    private TMetadata? ReadMetadata(ReadOnlyMemory<byte> json)
    {
        try
        {
            return PluginJson.Read(_metadata, json.Span);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

/// <summary><see cref="PluginServiceFamily{TRequest, TResponse, TMetadata}.Find" /> 找到的一个服务。</summary>
public sealed class PluginServiceEntry<TRequest, TResponse, TMetadata>
{
    private readonly PluginJsonType<TRequest> _request;
    private readonly PluginJsonType<TResponse> _response;

    internal PluginServiceEntry(string name, string shortName, string providerId, TMetadata? metadata,
        PluginJsonType<TRequest> request, PluginJsonType<TResponse> response)
    {
        Name = name;
        ShortName = shortName;
        ProviderId = providerId;
        Metadata = metadata;
        _request = request;
        _response = response;
    }

    /// <summary>完整服务名。</summary>
    public string Name { get; }

    /// <summary>去掉前缀的名称。</summary>
    public string ShortName { get; }

    /// <summary>提供方插件 ID。</summary>
    public string ProviderId { get; }

    public TMetadata? Metadata { get; }

    public Task<TResponse?> TryCallAsync(IBotContext context, TRequest request,
        CancellationToken cancellationToken = default)
    {
        return PluginServiceCalls.TryCallAsync(context, Name, _request, _response, request, cancellationToken);
    }

    public Task<TResponse> CallAsync(IBotContext context, TRequest request,
        CancellationToken cancellationToken = default)
    {
        return PluginServiceCalls.CallAsync(context, Name, _request, _response, request, cancellationToken);
    }
}

/// <summary>插件之间的事件。一般在契约类里声明为属性，由 SDK 生成实例。</summary>
public sealed class PluginEvent<T>
{
    private readonly PluginJsonType<T> _payload;

    public PluginEvent(string topic, PluginJsonType<T> payload)
    {
        PluginServiceNames.ValidateService(topic);
        Topic = topic;
        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public string Topic { get; }

    /// <summary>订阅，随插件卸载自动退订；返回的对象 Dispose 后提前退订。</summary>
    public IDisposable On(IBotContext context, Func<T, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        return context.Bus.OnUtf8(Topic, (payload, ct) => handler(PluginJson.Read(_payload, payload.Span)!, ct));
    }

    /// <summary>发布，等所有订阅者处理完，返回成功处理的数量。</summary>
    public Task<int> EmitAsync(IBotContext context, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Bus.EmitUtf8Async(Topic, PluginJson.Write(_payload, payload), cancellationToken);
    }
}

/// <summary>生成 JSON Schema，例如把参数格式告诉调用方或交给 AI 模型。</summary>
public static class PluginJsonSchema
{
    /// <summary>契约类型 <typeparamref name="T" /> 的 JSON Schema：字段类型、必填字段、默认值和 <c>[Description]</c> 说明。</summary>
    public static JsonObject For<T>()
    {
        return (JsonObject)JsonNode.Parse(PluginJson.TypeOf<T>().Schema)!;
    }
}

internal static class PluginServiceCalls
{
    public static async Task<TResponse?> TryCallAsync<TRequest, TResponse>(IBotContext context, string name,
        PluginJsonType<TRequest> requestType, PluginJsonType<TResponse> responseType, TRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CallAsync(context, name, requestType, responseType, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PluginServiceUnavailableException)
        {
            return default;
        }
    }

    public static async Task<TResponse> CallAsync<TRequest, TResponse>(IBotContext context, string name,
        PluginJsonType<TRequest> requestType, PluginJsonType<TResponse> responseType, TRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = await context.Services
            .CallUtf8Async(name, PluginJson.Write(requestType, request), cancellationToken)
            .ConfigureAwait(false);
        return PluginJson.Read(responseType, response.Span)!;
    }
}

internal static class PluginServiceNames
{
    public static void ValidateService(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('*'))
            throw new ArgumentException($"服务名 {name} 不能包含 *；一类服务用 PluginServiceFamily 声明。", nameof(name));
    }

    public static string FamilyPrefix(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (!pattern.EndsWith('*') || pattern.IndexOf('*') != pattern.Length - 1 || pattern.Length < 2)
            throw new ArgumentException($"一类服务的名称 {pattern} 应以 * 结尾，例如 weather.source.*。", nameof(pattern));
        return pattern[..^1];
    }
}