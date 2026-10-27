namespace ShiroBot.SDK.Core;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BotPluginAttribute(string id) : Attribute
{
    public string Id { get; } = id;

    public string Name { get; init; } = id;

    public string Version { get; init; } = "1.0.0";
    public string? Description { get; init; }
    public string? Author { get; init; }
    public PluginCategory Category { get; init; } = PluginCategory.Other;
    public string? GithubRepo { get; init; }
    public bool IsPluginSingleFile { get; init; }

    /// <summary>
    /// Semicolon-separated assembly names containing contracts shared with other plugins.
    /// The contract DLLs must be shipped beside the plugin and are loaded into the Default ALC.
    /// </summary>
    public string? SharedAssemblies { get; init; }

    /// <summary>
    /// Semicolon-separated plugin IDs that must be loaded before this plugin.
    /// </summary>
    public string? Dependencies { get; init; }

    /// <summary>
    /// Dispatch order for events: smaller values run first, and plugins sharing a value run in parallel.
    /// Once a plugin marks an event handled (see <see cref="ShiroBot.SDK.Plugin.BotEventHandling"/>),
    /// plugins with a larger value no longer receive it. Owners can override it per plugin.
    /// </summary>
    public int Priority { get; init; } = DefaultPriority;

    /// <summary>The priority of plugins that declare none, including those built before priorities existed.</summary>
    public const int DefaultPriority = 2;
}
