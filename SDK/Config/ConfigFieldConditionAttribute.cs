namespace ShiroBot.SDK.Config;

public enum ConfigConditionOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5
}

/// <summary>Shows a field only when another config key satisfies the condition.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ConfigVisibleWhenAttribute(
    string field,
    ConfigConditionOperator comparison,
    string value) : Attribute
{
    public string Field { get; } = field;
    public ConfigConditionOperator Comparison { get; } = comparison;
    public string Value { get; } = value;
}

/// <summary>Enables a field only when another config key satisfies the condition.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ConfigEnabledWhenAttribute(
    string field,
    ConfigConditionOperator comparison,
    string value) : Attribute
{
    public string Field { get; } = field;
    public ConfigConditionOperator Comparison { get; } = comparison;
    public string Value { get; } = value;
}
