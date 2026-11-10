using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ShiroBot.Plugins;

/// <summary>Copy config values into ordinary containers before JSON sees any collectible types.</summary>
internal static class ConfigFingerprintValue
{
    public static object? Create(object? value)
    {
        return Create(value, 0);
    }

    // The recursive depth limit bounds traversal of untrusted component configuration.
    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    private static object? Create(object? value, int depth)
    {
        if (depth > 64) throw new InvalidOperationException("Config fingerprint exceeds the supported nesting depth.");
        if (value is null) return null;
        var type = value.GetType();
        if (type.IsEnum) return Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
        if (value is string || type.IsPrimitive ||
            value is decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Uri)
            return value;
        switch (value)
        {
            case IDictionary dictionary:
            {
                var result = new Dictionary<string, object?>();
                // Preserve the concrete enumerator and ordered parsing/early-exit behavior.
                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator, ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (DictionaryEntry item in dictionary)
                    result.Add(Convert.ToString(item.Key, CultureInfo.InvariantCulture)!,
                        Create(item.Value, depth + 1));
                return result;
            }
            case IEnumerable sequence:
                return sequence.Cast<object?>().Select(item => Create(item, depth + 1)).ToArray();
        }

        var properties = new Dictionary<string, object?>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>();
            if (ignore?.Condition == JsonIgnoreCondition.Always) continue;
            var item = property.GetValue(value);
            if (item is null && ignore?.Condition == JsonIgnoreCondition.WhenWritingNull) continue;
            properties.Add(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name,
                Create(item, depth + 1));
        }

        return properties;
    }
}