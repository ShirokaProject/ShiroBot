using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Context;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Marketplace;
using ShiroBot.Update;
using ShiroBot.Console;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;
using Tomlyn;
using Tomlyn.Model;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static PluginConfigTarget? FindPluginForConfig(PluginManager pluginManager, string id)
    {
        var loaded = FindLoadedPlugin(pluginManager, id);
        if (loaded is not null)
        {
            return new PluginConfigTarget(loaded.Name, loaded.AssemblyPath);
        }

        var candidate = pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var info = pluginManager.TryProbePluginInfoFile(candidate);
            return new PluginConfigTarget(info?.Id ?? id, candidate);
        }

        var disabled = FindDisabledPluginFile(pluginManager, id);
        if (!string.IsNullOrWhiteSpace(disabled))
        {
            var info = TryProbeDisabledPluginInfo(pluginManager, disabled);
            return new PluginConfigTarget(info?.Id ?? id, disabled);
        }

        return null;
    }

    private static string GetPluginConfigPath(PluginManager pluginManager, string assemblyPath, string pluginId)
    {
        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        if (fullAssemblyPath.EndsWith(DisabledPluginSuffix, StringComparison.OrdinalIgnoreCase))
        {
            fullAssemblyPath = fullAssemblyPath[..^DisabledPluginSuffix.Length];
        }

        var pluginRoot = Path.GetFullPath(pluginManager.PluginRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assemblyDirectory = Path.GetFullPath(Path.GetDirectoryName(fullAssemblyPath) ?? pluginRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var configDirectory = string.Equals(assemblyDirectory, pluginRoot, pathComparison)
            ? Path.Combine(pluginRoot, pluginId)
            : assemblyDirectory;

        return Path.Combine(configDirectory, "config.toml");
    }

    internal static IReadOnlyDictionary<string, object?> LoadTomlObject(string configPath)
    {
        if (!File.Exists(configPath)) return new Dictionary<string, object?>();

        var model = TomlSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(configPath));
        return model?.ToDictionary(pair => pair.Key, pair => ConvertTomlValue(pair.Value))
               ?? new Dictionary<string, object?>();
    }

    private static object? ConvertTomlValue(object? value) => value switch
    {
        TomlTable table => table.ToDictionary(pair => pair.Key, pair => ConvertTomlValue(pair.Value)),
        TomlTableArray tables => tables.Select(ConvertTomlValue).ToArray(),
        TomlArray array => array.Select(ConvertTomlValue).ToArray(),
        _ => value
    };

    internal static object[] GetComponentConfigSchema(string assemblyPath)
    {
        if (!File.Exists(assemblyPath)) return [];

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return [];

            var reader = peReader.GetMetadataReader();
            TypeDefinitionHandle configTypeHandle = default;
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                var explicitlyMarked = type.GetCustomAttributes().Any(attributeHandle =>
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);
                    return GetCustomAttributeTypeName(reader, attribute.Constructor)
                        .EndsWith("ConfigModelAttribute", StringComparison.Ordinal);
                });
                if (explicitlyMarked)
                {
                    configTypeHandle = typeHandle;
                    break;
                }

                if (reader.GetString(type.Name).Equals("PluginConfig", StringComparison.OrdinalIgnoreCase))
                {
                    configTypeHandle = typeHandle;
                    break;
                }

                if (configTypeHandle.IsNil && type.GetProperties().Any(propertyHandle =>
                    ReadConfigFieldAttribute(reader, reader.GetPropertyDefinition(propertyHandle).GetCustomAttributes()) is not null))
                {
                    configTypeHandle = typeHandle;
                }
            }

            if (!configTypeHandle.IsNil)
            {
                var configType = reader.GetTypeDefinition(configTypeHandle);
                var typeNamespace = reader.GetString(configType.Namespace);
                var typeName = reader.GetString(configType.Name);
                var configTypeName = string.IsNullOrEmpty(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";
                var propertyHandles = configType.GetProperties().ToArray();
                var needsRuntimeDefaults = propertyHandles.Any(propertyHandle =>
                {
                    var attributes = reader.GetPropertyDefinition(propertyHandle).GetCustomAttributes();
                    return ReadConfigFieldAttribute(reader, attributes)?.Default is null;
                });
                var runtimeDefaults = needsRuntimeDefaults
                    ? ReadConfigModelDefaults(assemblyPath, configTypeName)
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                return propertyHandles
                    .Select(propertyHandle => CreateConfigSchemaItem(reader, reader.GetPropertyDefinition(propertyHandle), runtimeDefaults))
                    .Where(item => item is not null)
                    .Cast<object>()
                    .ToArray();
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return [];
    }

    private static ConfigSchemaItem? CreateConfigSchemaItem(
        MetadataReader reader,
        PropertyDefinition property,
        IReadOnlyDictionary<string, object?> runtimeDefaults)
    {
        var propertyName = reader.GetString(property.Name);
        if (string.IsNullOrWhiteSpace(propertyName)) return null;

        var field = ReadConfigFieldAttribute(reader, property.GetCustomAttributes());
        var key = NormalizeConfigKey(propertyName);
        var valueType = InferConfigPropertyType(reader, property);
        var enumOptions = IsEnumProperty(reader, property) ? field?.Options : null;
        var type = string.IsNullOrWhiteSpace(field?.Type)
            ? valueType
            : field.Type!;

        return new ConfigSchemaItem(
            key,
            string.IsNullOrWhiteSpace(field?.Label) ? propertyName : field.Label!,
            type,
            field?.Description ?? string.Empty,
            field?.Placeholder,
            field?.Options ?? [],
            field is not null && !double.IsNaN(field.Min) ? field.Min : null,
            field is not null && !double.IsNaN(field.Max) ? field.Max : null,
            field?.GroupLabel ?? field?.Group,
            field?.Group,
            field?.GroupLabel,
            field?.Order,
            field?.GroupOrder,
            ReadConfigFieldConditions(reader, property.GetCustomAttributes()),
            field?.Default is { } explicitDefault
                ? ParseConfigDefaultValue(explicitDefault, valueType, enumOptions)
                : runtimeDefaults.TryGetValue(key, out var initializedValue)
                    ? initializedValue
                    : GetTypeSystemDefault(valueType),
            valueType);
    }

    private static object? ParseConfigDefaultValue(object value, string valueType, string[]? enumOptions = null)
    {
        if (value is not string text)
        {
            if (valueType == "string" && value is IConvertible)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            return ConvertSchemaDefault(value);
        }
        switch (valueType)
        {
            case "boolean":
                return bool.Parse(text);
            case "integer":
                if (enumOptions is { Length: > 0 } &&
                    Array.FindIndex(enumOptions, option => string.Equals(option, text, StringComparison.Ordinal)) is var enumIndex && enumIndex >= 0)
                    return (long)enumIndex;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue)) return integerValue;
                if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedValue)) return unsignedValue;
                throw new InvalidOperationException($"配置字段默认值不是有效整数: {text}");
            case "number":
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numberValue)) return numberValue;
                throw new InvalidOperationException($"配置字段默认值不是有效数字: {text}");
            case "array":
                return JsonSerializer.Deserialize<JsonElement>(text);
            default:
                return text;
        }
    }

    private static object? GetTypeSystemDefault(string valueType) => valueType switch
    {
        "boolean" => false,
        "integer" => 0L,
        "number" => 0D,
        "string" => string.Empty,
        "array" => Array.Empty<object>(),
        _ => null
    };

    private static object? ConvertSchemaDefault(object? value) => value switch
    {
        null => null,
        Enum enumValue => enumValue.ToString(),
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
        System.Collections.IEnumerable values when value is not string => values.Cast<object?>().Select(ConvertSchemaDefault).ToArray(),
        _ => JsonSerializer.SerializeToElement(value, value.GetType())
    };

    private static Dictionary<string, object?> ReadConfigModelDefaults(string assemblyPath, string configTypeName)
    {
        var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var path = Path.GetFullPath(assemblyPath);
        if (path.EndsWith(DisabledPluginSuffix, StringComparison.OrdinalIgnoreCase))
            path = path[..^DisabledPluginSuffix.Length];
        if (!File.Exists(path)) return defaults;

        ConfigModelLoadContext? loadContext = null;
        try
        {
            loadContext = new ConfigModelLoadContext(path);
            var assembly = loadContext.LoadFromAssemblyPath(path);
            var configType = assembly.GetType(configTypeName, throwOnError: false, ignoreCase: false);
            if (configType is null) return defaults;

            object instance;
            try
            {
                instance = Activator.CreateInstance(configType)
                    ?? System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(configType);
            }
            catch (MissingMethodException)
            {
                // A model without a public parameterless constructor has no initialized instance values.
                // GetComponentConfigSchema will use CLR type defaults for its properties.
                return defaults;
            }

            foreach (var property in configType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                try
                {
                    defaults[NormalizeConfigKey(property.Name)] = ConvertSchemaDefault(property.GetValue(instance));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A computed property isn't a config default.
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return defaults;
        }
        finally
        {
            loadContext?.Unload();
        }

        return defaults;
    }

    private sealed class ConfigModelLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly string _assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        private readonly AssemblyDependencyResolver? _resolver = TryCreateResolver(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedSdk = typeof(ConfigModelAttribute).Assembly;
            if (string.Equals(assemblyName.Name, sharedSdk.GetName().Name, StringComparison.OrdinalIgnoreCase))
                return sharedSdk;

            var dependencyPath = _resolver?.ResolveAssemblyToPath(assemblyName);
            if (dependencyPath is null && !string.IsNullOrWhiteSpace(assemblyName.Name))
            {
                var siblingPath = Path.Combine(_assemblyDirectory, assemblyName.Name + ".dll");
                if (File.Exists(siblingPath)) dependencyPath = siblingPath;
            }
            return dependencyPath is null ? null : LoadFromAssemblyPath(dependencyPath);
        }

        private static AssemblyDependencyResolver? TryCreateResolver(string path)
        {
            try { return new AssemblyDependencyResolver(path); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException)
            {
                return null;
            }
        }
    }

    private static ConfigFieldConditionSchema[] ReadConfigFieldConditions(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
    {
        var conditions = new List<ConfigFieldConditionSchema>();
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = GetCustomAttributeTypeName(reader, attribute.Constructor);
            var effect = attributeTypeName.EndsWith("ConfigVisibleWhenAttribute", StringComparison.Ordinal)
                ? "visible"
                : attributeTypeName.EndsWith("ConfigEnabledWhenAttribute", StringComparison.Ordinal)
                    ? "enabled"
                    : null;
            if (effect is null) continue;

            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) continue;
            var field = blob.ReadSerializedString();
            var comparison = blob.ReadInt32();
            var value = blob.ReadSerializedString();
            var namedCount = blob.ReadUInt16();
            if (namedCount != 0 || string.IsNullOrWhiteSpace(field) || value is null) continue;

            var operation = comparison switch
            {
                0 => "eq",
                1 => "ne",
                2 => "gt",
                3 => "gte",
                4 => "lt",
                5 => "lte",
                _ => null
            };
            if (operation is null) continue;
            conditions.Add(new ConfigFieldConditionSchema(
                effect,
                NormalizeConfigKey(field),
                operation,
                value));
        }

        return [.. conditions];
    }

    private static ConfigFieldMetadata? ReadConfigFieldAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = GetCustomAttributeTypeName(reader, attribute.Constructor);
            if (!attributeTypeName.EndsWith("ConfigFieldAttribute", StringComparison.Ordinal)) continue;

            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) return null;

            var description = blob.ReadSerializedString() ?? string.Empty;
            var metadata = new ConfigFieldMetadata { Description = description };
            var namedCount = blob.ReadUInt16();
            for (var i = 0; i < namedCount; i++)
            {
                _ = blob.ReadByte();
                var typeCode = blob.ReadByte();
                byte? arrayElementType = null;
                if (typeCode == SerializedTypeSzArray)
                {
                    arrayElementType = blob.ReadByte();
                }

                var memberName = blob.ReadSerializedString();
                switch (memberName)
                {
                    case nameof(ConfigFieldMetadata.Label) when typeCode == SerializedTypeString:
                        metadata.Label = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Type) when typeCode == SerializedTypeString:
                        metadata.Type = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Placeholder) when typeCode == SerializedTypeString:
                        metadata.Placeholder = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Group) when typeCode == SerializedTypeString:
                        metadata.Group = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.GroupLabel) when typeCode == SerializedTypeString:
                        metadata.GroupLabel = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Order) when typeCode == SerializedTypeI4:
                        metadata.Order = blob.ReadInt32();
                        break;
                    case nameof(ConfigFieldMetadata.GroupOrder) when typeCode == SerializedTypeI4:
                        metadata.GroupOrder = blob.ReadInt32();
                        break;
                    case nameof(ConfigFieldMetadata.Default) when typeCode == SerializedTypeObject:
                        metadata.Default = ReadConfigAttributeValue(ref blob, blob.ReadByte());
                        break;
                    case nameof(ConfigFieldMetadata.Default) when typeCode == SerializedTypeString:
                        metadata.Default = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Options) when typeCode == SerializedTypeSzArray && arrayElementType == SerializedTypeString:
                        metadata.Options = ReadStringArray(ref blob);
                        break;
                    case nameof(ConfigFieldMetadata.Min) when typeCode == SerializedTypeR8:
                        metadata.Min = blob.ReadDouble();
                        break;
                    case nameof(ConfigFieldMetadata.Max) when typeCode == SerializedTypeR8:
                        metadata.Max = blob.ReadDouble();
                        break;
                    default:
                        SkipConfigAttributeValue(ref blob, typeCode, arrayElementType);
                        break;
                }
            }

            return metadata;
        }

        return null;
    }

    private static object? ReadConfigAttributeValue(ref BlobReader blob, byte typeCode)
    {
        if (typeCode == SerializedTypeSzArray)
        {
            var elementType = blob.ReadByte();
            var count = blob.ReadUInt32();
            if (count == uint.MaxValue) return null;
            var values = new object?[count];
            for (var index = 0; index < values.Length; index++)
                values[index] = ReadConfigAttributeValue(ref blob, elementType);
            return values;
        }

        return typeCode switch
        {
            SerializedTypeBoolean => blob.ReadByte() != 0,
            SerializedTypeI1 => blob.ReadSByte(),
            SerializedTypeU1 => blob.ReadByte(),
            SerializedTypeI2 => blob.ReadInt16(),
            SerializedTypeU2 => blob.ReadUInt16(),
            SerializedTypeI4 => blob.ReadInt32(),
            SerializedTypeU4 => blob.ReadUInt32(),
            SerializedTypeI8 => blob.ReadInt64(),
            SerializedTypeU8 => blob.ReadUInt64(),
            SerializedTypeR4 => blob.ReadSingle(),
            SerializedTypeR8 => blob.ReadDouble(),
            SerializedTypeString => blob.ReadSerializedString(),
            SerializedTypeEnum => ReadConfigEnumAttributeValue(ref blob),
            _ => null
        };
    }

    private static object ReadConfigEnumAttributeValue(ref BlobReader blob)
    {
        _ = blob.ReadSerializedString();
        return blob.ReadInt32();
    }

    private static string GetCustomAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        return constructor.Kind switch
        {
            HandleKind.MemberReference => GetTypeName(reader, reader.GetMemberReference((MemberReferenceHandle)constructor).Parent),
            HandleKind.MethodDefinition => GetTypeName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
            _ => string.Empty
        };
    }

    private static string GetTypeName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
            _ => string.Empty
        };
    }

    private static string InferConfigPropertyType(MetadataReader reader, PropertyDefinition property)
    {
        var blob = reader.GetBlobReader(property.Signature);
        _ = blob.ReadByte();
        _ = blob.ReadCompressedInteger();
        var elementType = blob.ReadByte();
        if (elementType == ElementTypeValueType && IsEnumType(reader, ReadTypeDefOrRefHandle(ref blob)))
            return "integer";

        return elementType switch
        {
            ElementTypeBoolean => "boolean",
            ElementTypeI1 or ElementTypeU1 or ElementTypeI2 or ElementTypeU2 or ElementTypeI4 or ElementTypeU4 or ElementTypeI8 or ElementTypeU8 => "integer",
            ElementTypeR4 or ElementTypeR8 => "number",
            ElementTypeSzArray => "array",
            _ => "string"
        };
    }

    private static bool IsEnumProperty(MetadataReader reader, PropertyDefinition property)
    {
        var blob = reader.GetBlobReader(property.Signature);
        _ = blob.ReadByte();
        _ = blob.ReadCompressedInteger();
        return blob.ReadByte() == ElementTypeValueType && IsEnumType(reader, ReadTypeDefOrRefHandle(ref blob));
    }

    private static EntityHandle ReadTypeDefOrRefHandle(ref BlobReader blob)
    {
        var encoded = blob.ReadCompressedInteger();
        var rowId = encoded >> 2;
        return (encoded & 0x3) switch
        {
            0 => MetadataTokens.TypeDefinitionHandle(rowId),
            1 => MetadataTokens.TypeReferenceHandle(rowId),
            2 => MetadataTokens.TypeSpecificationHandle(rowId),
            _ => default
        };
    }

    private static bool IsEnumType(MetadataReader reader, EntityHandle typeHandle)
    {
        if (typeHandle.Kind != HandleKind.TypeDefinition) return false;
        return reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle).GetFields()
            .Any(field => string.Equals(reader.GetString(reader.GetFieldDefinition(field).Name), "value__", StringComparison.Ordinal));
    }

    private static string[] ReadStringArray(ref BlobReader blob)
    {
        var count = blob.ReadUInt32();
        if (count == uint.MaxValue) return [];

        var values = new string[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = blob.ReadSerializedString() ?? string.Empty;
        }

        return values;
    }

    private static void SkipConfigAttributeValue(ref BlobReader blob, byte typeCode, byte? arrayElementType)
    {
        if (typeCode == SerializedTypeSzArray)
        {
            var count = blob.ReadUInt32();
            if (count == uint.MaxValue) return;
            for (var i = 0; i < count; i++) SkipConfigAttributeValue(ref blob, arrayElementType ?? SerializedTypeString, null);
            return;
        }

        switch (typeCode)
        {
            case SerializedTypeBoolean:
            case SerializedTypeI1:
            case SerializedTypeU1:
                blob.ReadByte();
                break;
            case SerializedTypeI2:
            case SerializedTypeU2:
                blob.ReadUInt16();
                break;
            case SerializedTypeI4:
            case SerializedTypeU4:
            case SerializedTypeR4:
                blob.ReadBytes(4);
                break;
            case SerializedTypeI8:
            case SerializedTypeU8:
            case SerializedTypeR8:
                blob.ReadBytes(8);
                break;
            case SerializedTypeString:
                blob.ReadSerializedString();
                break;
        }
    }

    internal static void ApplyComponentConfigPatch(
        ConfigManager configManager,
        string configPath,
        JsonElement patch,
        IEnumerable<object> schema)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("config 必须是对象。");
        }

        var fields = schema.OfType<ConfigSchemaItem>()
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        ApplyComponentConfigPatchEntries(configManager, configPath, fields, patch, string.Empty);
    }

    private static void ApplyComponentConfigPatchEntries(
        ConfigManager configManager,
        string configPath,
        IReadOnlyDictionary<string, ConfigSchemaItem> schema,
        JsonElement patch,
        string prefix)
    {
        foreach (var property in patch.EnumerateObject())
        {
            var segment = NormalizeConfigKey(property.Name);
            var key = prefix.Length == 0 ? segment : $"{prefix}.{segment}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                ApplyComponentConfigPatchEntries(configManager, configPath, schema, property.Value, key);
                continue;
            }

            var value = ConvertJsonValue(property.Value);
            if (schema.TryGetValue(key, out var field)) ValidateConfigValue(field, key, value);
            configManager.SetConfigValue(configPath, key, value);
        }
    }

    private static void ValidateConfigValue(ConfigSchemaItem field, string key, object? value)
    {
        var valid = field.ValueType switch
        {
            "boolean" => value is bool,
            "integer" => TryGetDouble(value, out var integer) && Math.Truncate(integer) == integer,
            "number" => TryGetDouble(value, out _),
            "array" => value is object?[],
            "string" => value is string,
            _ => true
        };
        if (value is null && field.ValueType == "string") valid = true;
        if (!valid)
            throw new InvalidOperationException($"配置项 {key} 的值类型不符合 Schema ({field.ValueType})。");

        if (field.Options.Length > 0 && value is not null)
        {
            var optionValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            var optionMatches = field.Options.Contains(optionValue, StringComparer.Ordinal);
            // For enum properties, ConfigField.Options contains member labels while the
            // serialized config value is the enum's numeric ordinal (0, 1, ...).
            if (!optionMatches && field.Type == "select" && field.ValueType == "integer" &&
                TryGetDouble(value, out var enumValue) && Math.Truncate(enumValue) == enumValue &&
                enumValue >= 0 && enumValue < field.Options.Length)
            {
                optionMatches = true;
            }

            if (!optionMatches)
                throw new InvalidOperationException($"配置项 {key} 的值不在允许选项中。");
        }

        if (field.Min is not null || field.Max is not null)
        {
            if (value is null || !TryGetDouble(value, out var number))
                throw new InvalidOperationException($"配置项 {key} 必须是数字。");
            if (field.Min is { } lower && number < lower || field.Max is { } upper && number > upper)
                throw new InvalidOperationException($"配置项 {key} 超出允许范围。");
        }
    }

    private static void ApplyPluginRoutePatch(
        ConfigManager configManager,
        string coreConfigPath,
        PluginRouteConfig routePolicy,
        string pluginId,
        JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("routes 必须是对象。");
        }

        var current = routePolicy.Plugins.TryGetValue(pluginId, out var existing) ? existing : routePolicy.Default;
        var mode = current.Mode;
        var groups = current.Groups;

        if (TryGetString(patch, "mode", out var patchedMode))
        {
            mode = NormalizePluginRouteMode(patchedMode);
        }

        if (TryGetIdArray(patch, "groups", out var patchedGroups))
        {
            groups = patchedGroups;
        }

        var rule = new PluginRouteRuleConfig { Mode = mode, Groups = groups };
        routePolicy.Plugins[pluginId] = rule;
        configManager.SetConfigValue(coreConfigPath, $"plugin_routes.plugins.{pluginId}.mode", rule.Mode);
        configManager.SetConfigValue(coreConfigPath, $"plugin_routes.plugins.{pluginId}.groups", rule.Groups);
    }

    private static object CreatePluginRouteResponse(PluginRouteConfig routePolicy, string pluginId)
    {
        var configured = routePolicy.Plugins.TryGetValue(pluginId, out var rule) &&
                         !string.Equals(rule.Mode, "default", StringComparison.OrdinalIgnoreCase);
        var effective = configured ? rule! : routePolicy.Default;
        return new
        {
            configured,
            mode = configured ? effective.Mode : "default",
            groups = configured ? effective.Groups : [],
            effective_mode = effective.Mode,
            effective_groups = effective.Groups,
            default_mode = routePolicy.Default.Mode,
            default_groups = routePolicy.Default.Groups
        };
    }

    private static string NormalizePluginRouteMode(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "whitelist" => "whitelist",
        "blacklist" => "blacklist",
        "default" => "default",
        _ => throw new InvalidOperationException("routes.mode 只能是 default、whitelist 或 blacklist。")
    };

    private static string NormalizePluginActionTone(string? tone) => tone?.Trim().ToLowerInvariant() switch
    {
        "primary" => "primary",
        "danger" => "danger",
        _ => "default"
    };

    private static string NormalizeConfigKey(string key)
    {
        if (key.Contains('_')) return key.Trim().ToLowerInvariant();

        var chars = new List<char>(key.Length + 4);
        foreach (var ch in key.Trim())
        {
            if (char.IsUpper(ch) && chars.Count > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(ch));
        }

        return new string(chars.ToArray());
    }

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToArray(),
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("插件配置值只能是字符串、数字、布尔值、数组或 null。")
    };

    private static bool TryGetDouble(object? value, out double number)
    {
        switch (value)
        {
            case double doubleValue:
                number = doubleValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                return true;
            default:
                number = 0;
                return false;
        }
    }

}
