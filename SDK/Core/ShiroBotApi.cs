namespace ShiroBot.SDK.Core;

/// <summary>ShiroBot public plugin and adapter API compatibility version.</summary>
public static class ShiroBotApi
{
    /// <summary>The API version implemented by this SDK and its matching host.</summary>
    public const string CurrentVersion = "1.0";
}

/// <summary>Legacy metadata retained for existing binaries. Current hosts ignore this declaration; use SDK and Model assembly ABI requirements.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ShiroBotApiCompatibilityAttribute(string minimumVersion, string maximumVersion) : Attribute
{
    public string MinimumVersion { get; } = minimumVersion;
    public string MaximumVersion { get; } = maximumVersion;
}
