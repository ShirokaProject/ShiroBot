namespace ShiroBot.SDK.Core;

/// <summary>ShiroBot public plugin and adapter API compatibility version.</summary>
public static class ShiroBotApi
{
    /// <summary>The API version implemented by this SDK and its matching host.</summary>
    public const string CurrentVersion = "0.8";
}

/// <summary>Declares the inclusive range of ShiroBot API versions supported by a component.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ShiroBotApiCompatibilityAttribute(string minimumVersion, string maximumVersion) : Attribute
{
    public string MinimumVersion { get; } = minimumVersion;
    public string MaximumVersion { get; } = maximumVersion;
}
