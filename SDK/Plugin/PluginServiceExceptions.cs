namespace ShiroBot.SDK.Plugin;

/// <summary>按名称调用的服务当前没有插件提供，或提供方正在卸载。</summary>
public sealed class PluginServiceUnavailableException(string serviceName)
    : InvalidOperationException($"Plugin service '{serviceName}' is not available.")
{
    public string ServiceName { get; } = serviceName;
}

/// <summary>
/// 按名称调用的服务在提供方抛出了异常。只携带异常类型名和消息文本，不引用提供方的异常对象，
/// 避免调用方保存异常时拉住提供方插件的程序集。
/// </summary>
public sealed class PluginServiceException(string serviceName, string remoteType, string remoteMessage)
    : Exception($"Plugin service '{serviceName}' failed: {remoteType}: {remoteMessage}")
{
    public string ServiceName { get; } = serviceName;
    public string RemoteType { get; } = remoteType;
    public string RemoteMessage { get; } = remoteMessage;
}
