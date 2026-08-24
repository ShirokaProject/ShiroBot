namespace ShiroBot.SDK.Core;

/// <summary>ShiroBot 可安装程序集的类别。</summary>
public enum ShiroBotPackageKind
{
    Model = 0,
    Adapter = 1,
    Plugin = 2
}

/// <summary>声明程序集内嵌的 ShiroBot 包元数据。</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ShiroBotPackageAttribute(string id, ShiroBotPackageKind kind, string version) : Attribute
{
    public string Id { get; } = id;
    public ShiroBotPackageKind Kind { get; } = kind;
    public string Version { get; } = version;
}

/// <summary>声明程序集需要已安装的平台 Model 包。</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RequiresShiroBotPackageAttribute(string packageId) : Attribute
{
    public string PackageId { get; } = packageId;

    /// <summary>所需最低版本；空值表示不限制。</summary>
    public string? MinimumVersion { get; init; }
}
