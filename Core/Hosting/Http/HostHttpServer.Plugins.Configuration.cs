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
        var configDirectory = string.Equals(assemblyDirectory, pluginRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(pluginRoot, pluginId)
            : assemblyDirectory;

        return Path.Combine(configDirectory, "config.toml");
    }

    private static void EnsureKnownPluginConfig(string pluginId, string configPath)
    {
        if (File.Exists(configPath)) return;
        if (!string.Equals(pluginId, "JmParser", StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, string.Join(Environment.NewLine, [
            "proxy = \"\"",
            "output_mode = \"file\"",
            "delete_after_minutes = 60",
            "max_concurrency = 16",
            "send_cover = true",
            "cover_blur_radius = 12"
        ]) + Environment.NewLine);
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

    private static object[] GetPluginConfigSchema(string assemblyPath)
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
                return reader.GetTypeDefinition(configTypeHandle).GetProperties()
                    .Select(propertyHandle => CreateConfigSchemaItem(reader, reader.GetPropertyDefinition(propertyHandle)))
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

    private static ConfigSchemaItem? CreateConfigSchemaItem(MetadataReader reader, PropertyDefinition property)
    {
        var propertyName = reader.GetString(property.Name);
        if (string.IsNullOrWhiteSpace(propertyName)) return null;

        var field = ReadConfigFieldAttribute(reader, property.GetCustomAttributes());
        var key = NormalizeConfigKey(propertyName);
        var type = string.IsNullOrWhiteSpace(field?.Type)
            ? InferConfigPropertyType(reader, property)
            : field.Type!;

        return new ConfigSchemaItem(
            key,
            string.IsNullOrWhiteSpace(field?.Label) ? propertyName : field.Label!,
            type,
            field?.Description ?? string.Empty,
            field?.Placeholder,
            field?.Options ?? [],
            field is not null && !double.IsNaN(field.Min) ? field.Min : null,
            field is not null && !double.IsNaN(field.Max) ? field.Max : null);
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
        return blob.ReadByte() switch
        {
            ElementTypeBoolean => "boolean",
            ElementTypeI1 or ElementTypeU1 or ElementTypeI2 or ElementTypeU2 or ElementTypeI4 or ElementTypeU4 or ElementTypeI8 or ElementTypeU8 or ElementTypeR4 or ElementTypeR8 => "number",
            _ => "string"
        };
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

    internal static void ApplyPluginConfigPatch(ConfigManager configManager, string pluginConfigPath, string pluginId, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("config 必须是对象。");
        }

        ApplyPluginConfigPatchEntries(configManager, pluginConfigPath, pluginId, patch, string.Empty);
    }

    private static void ApplyPluginConfigPatchEntries(
        ConfigManager configManager, string pluginConfigPath, string pluginId, JsonElement patch, string prefix)
    {
        foreach (var property in patch.EnumerateObject())
        {
            var segment = NormalizeConfigKey(property.Name);
            var key = prefix.Length == 0 ? segment : $"{prefix}.{segment}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                ApplyPluginConfigPatchEntries(configManager, pluginConfigPath, pluginId, property.Value, key);
                continue;
            }

            var value = ConvertJsonValue(property.Value);
            ValidatePluginConfigValue(pluginId, key, value);
            configManager.SetConfigValue(pluginConfigPath, key, value);
        }
    }

    private static void ValidatePluginConfigValue(string pluginId, string key, object? value)
    {
        if (!string.Equals(pluginId, "JmParser", StringComparison.OrdinalIgnoreCase)) return;

        switch (key)
        {
            case "proxy":
                if (value is not string) throw new InvalidOperationException("proxy 必须是字符串。");
                break;
            case "output_mode":
                var mode = value as string;
                if (mode is not ("file" or "url" or "both")) throw new InvalidOperationException("output_mode 只能是 file、url 或 both。");
                break;
            case "delete_after_minutes":
                if (!TryGetLong(value, out var deleteAfterMinutes) || deleteAfterMinutes < 0) throw new InvalidOperationException("delete_after_minutes 必须是大于等于 0 的数字。");
                break;
            case "max_concurrency":
                if (!TryGetLong(value, out var maxConcurrency) || maxConcurrency is < 1 or > 64) throw new InvalidOperationException("max_concurrency 必须在 1 到 64 之间。");
                break;
            case "send_cover":
                if (value is not bool) throw new InvalidOperationException("send_cover 必须是布尔值。");
                break;
            case "cover_blur_radius":
                if (!TryGetDouble(value, out var coverBlurRadius) || coverBlurRadius is < 0 or > 100) throw new InvalidOperationException("cover_blur_radius 必须在 0 到 100 之间。");
                break;
            default:
                throw new InvalidOperationException($"JmParser 不支持配置项: {key}");
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

    private static bool TryGetLong(object? value, out long number)
    {
        switch (value)
        {
            case long longValue:
                number = longValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case double doubleValue when Math.Abs(doubleValue % 1) < double.Epsilon:
                number = (long)doubleValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

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
