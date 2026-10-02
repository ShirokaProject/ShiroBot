namespace ShiroBot.SDK.Plugin;

/// <summary>一个已加载的适配器实例。</summary>
/// <param name="Id">实例 ID，传给 <see cref="IBotContext.UseInstance"/>；与平台账号 ID 不同。</param>
/// <param name="Name">实例显示名称，未设置时为实例 ID。</param>
/// <param name="PackageId">适配器包 ID，同一包的多个实例相同。</param>
/// <param name="AdapterName">适配器名称，如 "OneBot Adapter"。</param>
/// <param name="Version">适配器程序集版本。</param>
/// <param name="Platform">平台 ID，如 "qq"、"telegram"。</param>
/// <param name="Protocol">适配器声明的协议，如 "onebot"；未声明时为 null。</param>
public sealed record AdapterInstanceInfo(
    string Id,
    string Name,
    string PackageId,
    string AdapterName,
    string Version,
    string Platform,
    string? Protocol);
