namespace ShiroBot.SDK.Config;

[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigFieldAttribute(string description = "") : Attribute
{
    public string Description { get; } = description;

    public string? Label { get; init; }

    public string? Type { get; init; }

    public string[] Options { get; init; } = [];

    public double Min { get; init; } = double.NaN;

    public double Max { get; init; } = double.NaN;

    public string? Placeholder { get; init; }
}
