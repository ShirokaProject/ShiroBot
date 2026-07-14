namespace ShiroBot.SDK.Core;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BotAdapterAttribute(string id) : Attribute
{
    public string Id { get; } = id;
    public string Name { get; init; } = id;
    public string Version { get; init; } = "1.0.0";
    public string? Description { get; init; }
    public string? Author { get; init; }
    public string? GithubRepo { get; init; }
    public string? Protocol { get; init; }
    public string? ProtocolVersionRange { get; init; }
    public bool IsSingleFile { get; init; }
}
