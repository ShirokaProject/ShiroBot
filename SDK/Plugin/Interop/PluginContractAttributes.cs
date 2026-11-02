namespace ShiroBot.SDK.Plugin;

/// <summary>
/// 标在 <c>static partial class</c> 上，声明插件对外的契约：类里标了 <see cref="PluginServiceAttribute"/> 或
/// <see cref="PluginEventAttribute"/> 的 <c>static partial</c> 属性就是对外的服务和事件。
/// SDK 在编译时生成属性的实现和消息类型的序列化代码，并把契约写进插件 DLL，其他插件放入这个 DLL 即可调用。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginContractAttribute : Attribute
{
    /// <summary>从其他插件 DLL 导入的契约所在的程序集名；由 SDK 在导入时填写，不要手动设置。</summary>
    public string? ImportedFrom { get; init; }
}

/// <summary>
/// 标在契约类的 <see cref="PluginService{TRequest, TResponse}"/> / <see cref="PluginService{TResponse}"/> 属性上，指定服务名；
/// 标在 <see cref="PluginServiceFamily{TRequest, TResponse, TMetadata}"/> 属性上时用 <c>"前缀.*"</c> 表示一类服务。
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class PluginServiceAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>标在契约类的 <see cref="PluginEvent{T}"/> 属性上，指定事件主题。</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class PluginEventAttribute(string topic) : Attribute
{
    public string Topic { get; } = topic;
}

/// <summary>
/// 为不在任何契约服务里出现的类型生成序列化代码，例如只用于 <see cref="PluginJsonSchema.For{T}"/> 或
/// <see cref="PluginJson.As{T}"/> 的参数类型。契约服务用到的类型会自动生成，不需要标记。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class PluginJsonAttribute : Attribute;
