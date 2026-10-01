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

    /// <summary>
    /// Builds the config schema of an assembly that is loaded in the host, including the host itself.
    /// Metadata is read from memory, so this also works for single-file publishes where
    /// <see cref="Assembly.Location"/> is empty.
    /// </summary>
    internal static object[] GetComponentConfigSchema(Assembly loadedAssembly) =>
        GetComponentConfigSchema(null, loadedAssembly);

    /// <summary>
    /// Builds the config schema of a component. Initialized property defaults are only read when
    /// <paramref name="loadedAssembly"/> is supplied; unloaded component files are inspected as metadata only.
    /// </summary>
    internal static unsafe object[] GetComponentConfigSchema(string? assemblyPath, Assembly? loadedAssembly = null)
    {
        try
        {
            if (loadedAssembly is not null && loadedAssembly.TryGetRawMetadata(out var blob, out var length))
            {
                try
                {
                    return BuildComponentConfigSchema(new MetadataReader(blob, length), loadedAssembly);
                }
                finally
                {
                    GC.KeepAlive(loadedAssembly);
                }
            }

            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath)) return [];

            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return [];
            return BuildComponentConfigSchema(peReader.GetMetadataReader(), loadedAssembly);
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static object[] BuildComponentConfigSchema(MetadataReader reader, Assembly? loadedAssembly)
    {
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
            var runtimeDefaults = ReadConfigModelDefaults(loadedAssembly, configTypeName);
            return BuildConfigSchemaFields(reader, configTypeHandle, runtimeDefaults, [configTypeHandle])
                .Cast<object>()
                .ToArray();
        }

        return [];
    }

    private const int MaxConfigSectionDepth = 6;

    private static ConfigSchemaItem[] BuildConfigSchemaFields(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        IReadOnlyDictionary<string, object?> defaults,
        IReadOnlyList<TypeDefinitionHandle> path)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        return type.GetProperties()
            .Select(propertyHandle => CreateConfigSchemaItem(reader, reader.GetPropertyDefinition(propertyHandle), defaults, path))
            .Where(item => item is not null)
            .Cast<ConfigSchemaItem>()
            .ToArray();
    }

    private static ConfigSchemaItem? CreateConfigSchemaItem(
        MetadataReader reader,
        PropertyDefinition property,
        IReadOnlyDictionary<string, object?> runtimeDefaults,
        IReadOnlyList<TypeDefinitionHandle> path)
    {
        var propertyName = reader.GetString(property.Name);
        if (string.IsNullOrWhiteSpace(propertyName)) return null;

        var field = ReadConfigFieldAttribute(reader, property.GetCustomAttributes());
        var key = NormalizeConfigKey(propertyName);
        var shape = ReadConfigPropertyShape(reader, property);
        var valueType = shape.Kind;
        var enumOptions = shape.IsEnum ? field?.Options : null;
        var type = string.IsNullOrWhiteSpace(field?.Type)
            ? valueType
            : field.Type!;
        var defaultValue = field?.Default is { } explicitDefault
            ? ParseConfigDefaultValue(explicitDefault, valueType, enumOptions)
            : runtimeDefaults.TryGetValue(key, out var initializedValue)
                ? initializedValue
                : GetTypeSystemDefault(valueType);

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
            defaultValue,
            valueType)
        {
            Fields = ReadSectionFields(shape, defaultValue as IReadOnlyDictionary<string, object?>),
            ItemType = shape.Kind == "array" ? shape.Item?.Kind ?? "string" : null,
            ItemFields = shape.Kind == "array" && shape.Item is { } item ? ReadSectionFields(item, null) : null
        };

        ConfigSchemaItem[]? ReadSectionFields(ConfigTypeShape section, IReadOnlyDictionary<string, object?>? sectionDefaults)
        {
            if (section.Kind != "section" || section.Section.IsNil) return null;
            // A model that references itself would recurse forever; stop and let the editor fall back to JSON.
            if (path.Count >= MaxConfigSectionDepth || path.Contains(section.Section)) return [];
            return BuildConfigSchemaFields(
                reader,
                section.Section,
                sectionDefaults ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                [.. path, section.Section]);
        }
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

    /// <summary>
    /// Converts an initialized config value into the JSON shape the API uses: config objects become
    /// dictionaries keyed by their TOML (snake_case) names, so nested defaults line up with nested fields.
    /// </summary>
    private static object? ConvertSchemaDefault(object? value) => ConvertSchemaDefault(value, 0);

    private static object? ConvertSchemaDefault(object? value, int depth) => value switch
    {
        null => null,
        Enum enumValue => enumValue.ToString(),
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
        _ when depth >= MaxConfigSectionDepth => null,
        System.Collections.IDictionary map => map.Keys.Cast<object>().ToDictionary(
            key => Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty,
            key => ConvertSchemaDefault(map[key], depth + 1),
            StringComparer.OrdinalIgnoreCase),
        System.Collections.IEnumerable values => values.Cast<object?>().Select(item => ConvertSchemaDefault(item, depth + 1)).ToArray(),
        DateTime or DateTimeOffset or TimeSpan or Guid or Uri or Version => Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => NormalizeConfigKey(property.Name),
                property => ConvertSchemaDefault(property.GetValue(value), depth + 1),
                StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>
    /// Reads initialized property values from a config model that is already loaded in the host.
    /// Assemblies that are not loaded (disabled or not yet started components) are never loaded or
    /// executed just to render a schema; their fields fall back to explicit or CLR defaults.
    /// </summary>
    private static Dictionary<string, object?> ReadConfigModelDefaults(Assembly? loadedAssembly, string configTypeName)
    {
        var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (loadedAssembly is null) return defaults;

        try
        {
            var configType = loadedAssembly.GetType(configTypeName, throwOnError: false, ignoreCase: false);
            if (configType is null || configType.IsAbstract || configType.GetConstructor(Type.EmptyTypes) is null)
                return defaults;

            var instance = Activator.CreateInstance(configType)!;
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
            defaults.Clear();
        }

        return defaults;
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

    /// <summary>
    /// Shape of a config property for the editor: <c>section</c> is a nested config class with known
    /// fields, <c>object</c> a free-form table (dictionary), <c>array</c> a list of <see cref="Item"/>.
    /// </summary>
    private sealed record ConfigTypeShape(string Kind, TypeDefinitionHandle Section = default, ConfigTypeShape? Item = null, bool IsEnum = false);

    private static readonly HashSet<string> ConfigListTypeNames = new(StringComparer.Ordinal)
    {
        "List`1", "IList`1", "IReadOnlyList`1", "ICollection`1", "IReadOnlyCollection`1", "IEnumerable`1",
        "HashSet`1", "ISet`1", "IReadOnlySet`1", "SortedSet`1", "Collection`1", "ImmutableArray`1", "ImmutableList`1"
    };

    private static readonly HashSet<string> ConfigTextTypeNames = new(StringComparer.Ordinal)
    {
        "System.Uri", "System.Version", "System.DateTime", "System.DateTimeOffset", "System.TimeSpan",
        "System.Guid", "System.DateOnly", "System.TimeOnly", "System.Char", "System.String"
    };

    private static ConfigTypeShape ReadConfigPropertyShape(MetadataReader reader, PropertyDefinition property)
    {
        var blob = reader.GetBlobReader(property.Signature);
        _ = blob.ReadByte();
        _ = blob.ReadCompressedInteger();
        try
        {
            return ReadConfigTypeShape(reader, ref blob);
        }
        catch (BadImageFormatException)
        {
            return new ConfigTypeShape("string");
        }
    }

    private static ConfigTypeShape ReadConfigTypeShape(MetadataReader reader, ref BlobReader blob)
    {
        while (true)
        {
            var code = blob.ReadSignatureTypeCode();
            switch (code)
            {
                case SignatureTypeCode.RequiredModifier:
                case SignatureTypeCode.OptionalModifier:
                    _ = blob.ReadTypeHandle();
                    continue;
                case SignatureTypeCode.Boolean:
                    return new ConfigTypeShape("boolean");
                case SignatureTypeCode.SByte or SignatureTypeCode.Byte or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 or
                    SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or
                    SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr:
                    return new ConfigTypeShape("integer");
                case SignatureTypeCode.Single or SignatureTypeCode.Double:
                    return new ConfigTypeShape("number");
                case SignatureTypeCode.String or SignatureTypeCode.Char:
                    return new ConfigTypeShape("string");
                case SignatureTypeCode.Object:
                    return new ConfigTypeShape("object");
                case SignatureTypeCode.SZArray:
                    return new ConfigTypeShape("array", Item: ReadConfigTypeShape(reader, ref blob));
                case SignatureTypeCode.TypeHandle:
                    return ShapeOfNamedType(reader, blob.ReadTypeHandle());
                case SignatureTypeCode.GenericTypeInstance:
                {
                    _ = blob.ReadSignatureTypeCode();
                    var genericType = blob.ReadTypeHandle();
                    var count = blob.ReadCompressedInteger();
                    var arguments = new ConfigTypeShape[count];
                    for (var index = 0; index < count; index++) arguments[index] = ReadConfigTypeShape(reader, ref blob);
                    var (_, name) = GetConfigTypeName(reader, genericType);
                    if (name == "Nullable`1" && count == 1) return arguments[0];
                    if (ConfigListTypeNames.Contains(name) && count == 1) return new ConfigTypeShape("array", Item: arguments[0]);
                    return new ConfigTypeShape("object");
                }
                default:
                    return new ConfigTypeShape("string");
            }
        }
    }

    private static ConfigTypeShape ShapeOfNamedType(MetadataReader reader, EntityHandle handle)
    {
        if (IsEnumType(reader, handle)) return new ConfigTypeShape("integer", IsEnum: true);
        var (ns, name) = GetConfigTypeName(reader, handle);
        var fullName = ns.Length == 0 ? name : $"{ns}.{name}";
        if (fullName == "System.Decimal") return new ConfigTypeShape("number");
        if (ConfigTextTypeNames.Contains(fullName)) return new ConfigTypeShape("string");
        if (handle.Kind == HandleKind.TypeDefinition) return new ConfigTypeShape("section", (TypeDefinitionHandle)handle);
        // A type from another assembly: its fields are unknown here, so edit it as a free-form table.
        return new ConfigTypeShape("object");
    }

    private static (string Namespace, string Name) GetConfigTypeName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => (reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Namespace),
            reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name)),
        HandleKind.TypeReference => (reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Namespace),
            reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name)),
        _ => (string.Empty, string.Empty)
    };

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

        var fields = new Dictionary<string, ConfigSchemaItem>(StringComparer.OrdinalIgnoreCase);
        AddSchemaFields(fields, schema.OfType<ConfigSchemaItem>(), string.Empty);

        // Validate everything before writing anything, so a rejected save never leaves a half-written file.
        var writes = new List<(string Key, object? Value)>();
        CollectComponentConfigPatch(fields, patch, string.Empty, writes);

        var current = LoadTomlObject(configPath);
        foreach (var (key, value) in writes)
        {
            // The dashboard submits the whole config; unchanged values are skipped so untouched
            // tables, [[arrays]] and their formatting stay exactly as the user wrote them.
            var exists = TryGetConfigValue(current, key, out var existing);
            if (exists && ConfigValuesEqual(existing, value)) continue;
            // Tables and lists of tables have several TOML forms ([key], [[key]], inline) and must be
            // replaced as a whole; plain values and plain lists are updated in place.
            if (ContainsConfigTable(value) || exists && ContainsConfigTable(existing))
                configManager.ReplaceConfigValue(configPath, key, value);
            else
                configManager.SetConfigValue(configPath, key, value);
        }
    }

    private static void AddSchemaFields(Dictionary<string, ConfigSchemaItem> map, IEnumerable<ConfigSchemaItem> items, string prefix)
    {
        foreach (var item in items)
        {
            var key = prefix + item.Key;
            map.TryAdd(key, item);
            if (item.Fields is { } fields) AddSchemaFields(map, fields, key + ".");
        }
    }

    private static void CollectComponentConfigPatch(
        IReadOnlyDictionary<string, ConfigSchemaItem> schema,
        JsonElement patch,
        string prefix,
        List<(string Key, object? Value)> writes)
    {
        foreach (var property in patch.EnumerateObject())
        {
            var segment = NormalizeConfigKey(property.Name);
            var key = prefix.Length == 0 ? segment : $"{prefix}.{segment}";
            schema.TryGetValue(key, out var field);
            if (property.Value.ValueKind == JsonValueKind.Object && field?.ValueType != "object")
            {
                if (field is not null && field.ValueType != "section")
                    throw new InvalidOperationException($"配置项 {key} 的值类型不符合 Schema ({field.ValueType})。");
                // A known section is written field by field, keeping keys that the submitted object omits.
                CollectComponentConfigPatch(schema, property.Value, key, writes);
                continue;
            }

            var value = ConvertJsonValue(property.Value);
            if (field is not null) ValidateConfigValue(field, key, value);
            writes.Add((key, value));
        }
    }

    private static bool TryGetConfigValue(IReadOnlyDictionary<string, object?> config, string keyPath, out object? value)
    {
        value = null;
        object? node = config;
        foreach (var part in keyPath.Split('.'))
        {
            if (node is not IReadOnlyDictionary<string, object?> table && node is not IDictionary<string, object?>)
                return false;
            var found = node switch
            {
                IReadOnlyDictionary<string, object?> readOnly => readOnly.FirstOrDefault(pair => string.Equals(pair.Key, part, StringComparison.OrdinalIgnoreCase)),
                IDictionary<string, object?> writable => writable.FirstOrDefault(pair => string.Equals(pair.Key, part, StringComparison.OrdinalIgnoreCase)),
                _ => default
            };
            if (found.Key is null) return false;
            node = found.Value;
        }

        value = node;
        return true;
    }

    private static bool ConfigValuesEqual(object? left, object? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left is string || right is string)
            return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
                   left is string == right is string;
        if (left is bool leftBool || right is bool) return left is bool && right is bool rightBool && (bool)left == rightBool;
        if (IsConfigNumber(left) && IsConfigNumber(right))
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture) == Convert.ToDecimal(right, CultureInfo.InvariantCulture);
        if (left is IEnumerable<KeyValuePair<string, object?>> leftTable && right is IEnumerable<KeyValuePair<string, object?>> rightTable)
        {
            var a = leftTable.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var b = rightTable.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && ConfigValuesEqual(pair.Value, other));
        }
        if (left is System.Collections.IEnumerable leftItems && right is System.Collections.IEnumerable rightItems)
        {
            var a = leftItems.Cast<object?>().ToArray();
            var b = rightItems.Cast<object?>().ToArray();
            return a.Length == b.Length && a.Zip(b).All(pair => ConfigValuesEqual(pair.First, pair.Second));
        }

        return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static bool ContainsConfigTable(object? value) =>
        value is IEnumerable<KeyValuePair<string, object?>> ||
        value is System.Collections.IEnumerable items and not string &&
        items.Cast<object?>().Any(item => item is IEnumerable<KeyValuePair<string, object?>>);

    private static bool IsConfigNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static void ValidateConfigValue(ConfigSchemaItem field, string key, object? value)
    {
        var valid = field.ValueType switch
        {
            "boolean" => value is bool,
            // Numeric strings are rejected: they would be saved as TOML strings and fail typed loading.
            "integer" => value is not string && TryGetDouble(value, out var integer) && Math.Truncate(integer) == integer,
            "number" => value is not string && TryGetDouble(value, out _),
            "array" => value is object?[] items && items.All(item => IsValidConfigItem(field.ItemType, item)),
            "string" => value is string,
            "section" or "object" => value is IDictionary<string, object?>,
            _ => true
        };
        if (value is null && field.ValueType == "string") valid = true;
        if (!valid)
            throw new InvalidOperationException(field.ValueType == "array"
                ? $"配置项 {key} 必须是元素类型为 {field.ItemType ?? "string"} 的列表。"
                : $"配置项 {key} 的值类型不符合 Schema ({field.ValueType})。");

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

    private static bool IsValidConfigItem(string? itemType, object? item) => itemType switch
    {
        "boolean" => item is bool,
        "integer" => item is not string && TryGetDouble(item, out var integer) && Math.Truncate(integer) == integer,
        "number" => item is not string && TryGetDouble(item, out _),
        "string" => item is string,
        "section" or "object" => item is IDictionary<string, object?>,
        _ => true
    };

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToArray(),
        // Tables keep their submitted key order so a rewritten [[array]] reads like the original.
        JsonValueKind.Object => value.EnumerateObject().Aggregate(
            new OrderedDictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            (table, property) =>
            {
                table[property.Name] = ConvertJsonValue(property.Value);
                return table;
            }),
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("配置值只能是字符串、数字、布尔值、数组、对象或 null。")
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
