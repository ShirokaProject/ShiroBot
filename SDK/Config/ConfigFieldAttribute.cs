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

    public string? Group { get; init; }

    public string? GroupLabel { get; init; }

    public string? GroupIcon { get; init; }

    public string? GroupDescription { get; init; }

    public int Order { get; init; } = int.MaxValue;

    public int GroupOrder { get; init; } = int.MaxValue;

    /// <summary>
    /// Optional explicit default. When omitted, the host reads the config model's initialized
    /// property value and finally falls back to the property's CLR type default.
    /// </summary>
    public object? Default { get; init; }
}
