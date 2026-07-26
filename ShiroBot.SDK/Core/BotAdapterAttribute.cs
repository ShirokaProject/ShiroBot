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

    /// <summary>
    /// Semicolon-separated assembly names containing platform contracts shared with plugins
    /// (e.g. "ShiroBot.Discord.Contracts"). The contract DLLs must be shipped beside the
    /// adapter and are loaded into the Default ALC before the adapter itself, so plugins and
    /// the adapter observe identical types when probing extensions or casting Raw payloads.
    /// </summary>
    public string? SharedAssemblies { get; init; }
}
