namespace ShiroBot.SDK.Core;

public static class BotAdapterMetadata
{
    public static BotComponentMetadata Resolve(IBotAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        if (FromAttribute(adapter.GetType()) is { } attributed)
        {
            return attributed;
        }

#pragma warning disable CS0618
        return adapter.Metadata ?? FromAttribute(adapter.GetType(), adapter.Name)!;
#pragma warning restore CS0618
    }

    public static BotComponentMetadata FromAdapterType(Type adapterType, string fallbackName) =>
        FromAttribute(adapterType, fallbackName)!;

    private static BotComponentMetadata? FromAttribute(Type adapterType, string? fallbackName = null)
    {
        var attribute = Attribute.GetCustomAttribute(adapterType, typeof(BotAdapterAttribute), inherit: false)
            as BotAdapterAttribute;
        if (attribute is null)
        {
            return fallbackName is null
                ? null
                : new BotComponentMetadata { Id = fallbackName, Name = fallbackName };
        }

        return new BotComponentMetadata
        {
            Id = attribute.Id,
            Name = attribute.Name,
            Version = attribute.Version,
            Description = attribute.Description,
            Author = attribute.Author,
            GithubRepo = attribute.GithubRepo,
            Protocol = attribute.Protocol,
            ProtocolVersionRange = attribute.ProtocolVersionRange,
            IsSingleFile = attribute.IsSingleFile,
#pragma warning disable CS0618
            IsPluginSingleFile = attribute.IsSingleFile
#pragma warning restore CS0618
        };
    }
}
