using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ShiroBot.SDK.Core;

namespace ShiroBot.Adapters.Compatibility;

/// <summary>
/// 在加载适配器程序集之前,用元数据读取器预读 BotAdapterAttribute.SharedAssemblies。
/// 契约程序集必须先于适配器 ALC 注册进 Default ALC,插件与适配器才能看到同一份类型。
/// </summary>
internal static class AdapterContractProbe
{
    private const byte SerializedTypeBoolean = 0x02;
    private const byte SerializedTypeI4 = 0x08;
    private const byte SerializedTypeString = 0x0e;
    private const byte SerializedTypeEnum = 0x55;

    /// <summary>读取适配器声明的共享契约程序集名列表;无声明或读取失败返回空。</summary>
    public static IReadOnlyList<string> ReadSharedAssemblies(string adapterAssemblyPath)
        => ReadMetadata(adapterAssemblyPath)?.SharedAssemblies ?? [];

    public static AdapterProbeInfo? ReadMetadata(string adapterAssemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(adapterAssemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return null;

            var reader = peReader.GetMetadataReader();
            var compatibility = ReadApiCompatibility(reader);
            AdapterProbeInfo? result = null;
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if ((type.Attributes & TypeAttributes.Interface) != 0 ||
                    (type.Attributes & TypeAttributes.Abstract) != 0)
                {
                    continue;
                }

                RawAdapterAttribute? adapterAttribute = null;
                foreach (var attributeHandle in type.GetCustomAttributes())
                {
                    if (TryReadBotAdapterAttribute(reader, attributeHandle, out var value))
                    {
                        adapterAttribute = value;
                    }
                }

                if (adapterAttribute is not { } adapter) continue;
                if (result is not null)
                    throw new InvalidOperationException("程序集包含多个 BotAdapter 入口。");
                result = new AdapterProbeInfo(
                    adapter.Id,
                    adapter.SharedAssemblies,
                    compatibility.MinimumVersion,
                    compatibility.MaximumVersion,
                    adapter.Name,
                    adapter.Version,
                    adapter.Description,
                    adapter.Protocol);
            }

            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // 探测失败按无契约处理,加载阶段自然报错
        }

        return null;
    }

    private static bool TryReadBotAdapterAttribute(
        MetadataReader reader,
        CustomAttributeHandle attributeHandle,
        out RawAdapterAttribute attribute)
    {
        attribute = default;
        var customAttribute = reader.GetCustomAttribute(attributeHandle);
        if (!string.Equals(
                GetAttributeTypeFullName(reader, customAttribute),
                typeof(BotAdapterAttribute).FullName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var blob = reader.GetBlobReader(customAttribute.Value);
        if (blob.ReadUInt16() != 1) return false; // prolog

        var id = blob.ReadSerializedString(); // fixed arg: id
        if (string.IsNullOrWhiteSpace(id)) return false;

        string? sharedAssemblies = null;
        string? name = null;
        string? version = null;
        string? description = null;
        string? protocol = null;

        var namedArgumentCount = blob.ReadUInt16();
        for (var i = 0; i < namedArgumentCount; i++)
        {
            _ = blob.ReadByte(); // FIELD / PROPERTY
            var typeCode = blob.ReadByte();
            if (typeCode == SerializedTypeEnum)
            {
                _ = blob.ReadSerializedString();
            }

            var memberName = blob.ReadSerializedString();

            if (string.Equals(memberName, nameof(BotAdapterAttribute.SharedAssemblies), StringComparison.Ordinal) &&
                typeCode == SerializedTypeString)
            {
                sharedAssemblies = blob.ReadSerializedString();
                continue;
            }

            if (typeCode == SerializedTypeString)
            {
                var value = blob.ReadSerializedString();
                if (string.Equals(memberName, nameof(BotAdapterAttribute.Name), StringComparison.Ordinal)) name = value;
                else if (string.Equals(memberName, nameof(BotAdapterAttribute.Version), StringComparison.Ordinal)) version = value;
                else if (string.Equals(memberName, nameof(BotAdapterAttribute.Description), StringComparison.Ordinal)) description = value;
                else if (string.Equals(memberName, nameof(BotAdapterAttribute.Protocol), StringComparison.Ordinal)) protocol = value;
                continue;
            }

            // 跳过其他命名参数(BotAdapterAttribute 只有 string / bool 属性)
            switch (typeCode)
            {
                case SerializedTypeString:
                    _ = blob.ReadSerializedString();
                    break;
                case SerializedTypeBoolean:
                    _ = blob.ReadBoolean();
                    break;
                case SerializedTypeI4:
                    _ = blob.ReadInt32();
                    break;
                case SerializedTypeEnum:
                    _ = blob.ReadInt32();
                    break;
                default:
                    return false; // 未知类型,放弃解析
            }
        }

        var contracts = string.IsNullOrWhiteSpace(sharedAssemblies)
            ? []
            : sharedAssemblies.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        attribute = new RawAdapterAttribute(id, contracts, name, version, description, protocol);
        return true;
    }

    private static bool TryReadApiCompatibilityAttribute(
        MetadataReader reader,
        CustomAttributeHandle attributeHandle,
        out RawApiCompatibilityAttribute attribute)
    {
        attribute = default;
        var customAttribute = reader.GetCustomAttribute(attributeHandle);
        if (!string.Equals(
                GetAttributeTypeFullName(reader, customAttribute),
                typeof(ShiroBotApiCompatibilityAttribute).FullName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var blob = reader.GetBlobReader(customAttribute.Value);
        if (blob.ReadUInt16() != 1) return false;
        var minimumVersion = blob.ReadSerializedString();
        var maximumVersion = blob.ReadSerializedString();
        if (minimumVersion is null || maximumVersion is null) return false;
        attribute = new RawApiCompatibilityAttribute(minimumVersion, maximumVersion);
        return true;
    }

    private static RawApiCompatibilityAttribute ReadApiCompatibility(MetadataReader reader)
    {
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            if (TryReadApiCompatibilityAttribute(reader, attributeHandle, out var compatibility))
            {
                return compatibility;
            }
        }

        return new RawApiCompatibilityAttribute("0.8", "0.8");
    }

    private static string? GetAttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
            {
                var memberReference = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (memberReference.Parent.Kind != HandleKind.TypeReference) return null;
                var typeReference = reader.GetTypeReference((TypeReferenceHandle)memberReference.Parent);
                return $"{reader.GetString(typeReference.Namespace)}.{reader.GetString(typeReference.Name)}";
            }
            case HandleKind.MethodDefinition:
            {
                var methodDefinition = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var typeDefinition = reader.GetTypeDefinition(methodDefinition.GetDeclaringType());
                return $"{reader.GetString(typeDefinition.Namespace)}.{reader.GetString(typeDefinition.Name)}";
            }
            default:
                return null;
        }
    }

    internal sealed record AdapterProbeInfo(
        string Id,
        IReadOnlyList<string> SharedAssemblies,
        string MinimumApiVersion,
        string MaximumApiVersion,
        string? Name,
        string? Version,
        string? Description,
        string? Protocol);

    private readonly record struct RawAdapterAttribute(
        string Id, IReadOnlyList<string> SharedAssemblies, string? Name, string? Version, string? Description, string? Protocol);

    private readonly record struct RawApiCompatibilityAttribute(string MinimumVersion, string MaximumVersion);
}
