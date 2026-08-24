using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ShiroBot.SDK.Core;

namespace ShiroBot.Core;

/// <summary>在不加载程序集的前提下读取其 Model 包依赖。</summary>
internal static class PackageDependencyProbe
{
    private const byte SerializedTypeString = 0x0e;

    public static PackageMetadata? ReadPackage(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return null;

        var reader = peReader.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        foreach (var attributeHandle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!string.Equals(
                    GetAttributeTypeFullName(reader, attribute),
                    typeof(ShiroBotPackageAttribute).FullName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) return null;
            var id = blob.ReadSerializedString();
            var kind = (ShiroBotPackageKind)blob.ReadInt32();
            var version = blob.ReadSerializedString();
            return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)
                ? null
                : new PackageMetadata(id, kind, version);
        }

        return null;
    }

    public static IReadOnlyList<Requirement> ReadRequirements(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return [];

        var reader = peReader.GetMetadataReader();
        var requirements = new List<Requirement>();
        var assembly = reader.GetAssemblyDefinition();
        foreach (var attributeHandle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!string.Equals(
                    GetAttributeTypeFullName(reader, attribute),
                    typeof(RequiresShiroBotPackageAttribute).FullName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) continue;

            var packageId = blob.ReadSerializedString();
            if (string.IsNullOrWhiteSpace(packageId)) continue;

            string? minimumVersion = null;
            var namedArgumentCount = blob.ReadUInt16();
            for (var i = 0; i < namedArgumentCount; i++)
            {
                _ = blob.ReadByte(); // FIELD / PROPERTY
                var typeCode = blob.ReadByte();
                var memberName = blob.ReadSerializedString();
                if (typeCode != SerializedTypeString)
                {
                    throw new InvalidOperationException(
                        $"无法读取 {assemblyPath} 的程序集包依赖：不支持的 Attribute 参数类型。");
                }

                var value = blob.ReadSerializedString();
                if (string.Equals(memberName, nameof(RequiresShiroBotPackageAttribute.MinimumVersion), StringComparison.Ordinal))
                {
                    minimumVersion = value;
                }
            }

            requirements.Add(new Requirement(packageId, minimumVersion));
        }

        return requirements;
    }

    private static string? GetAttributeTypeFullName(MetadataReader reader, CustomAttribute attribute) =>
        attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => GetMemberReferenceAttributeType(reader, (MemberReferenceHandle)attribute.Constructor),
            HandleKind.MethodDefinition => GetMethodDefinitionAttributeType(reader, (MethodDefinitionHandle)attribute.Constructor),
            _ => null
        };

    private static string? GetMemberReferenceAttributeType(MetadataReader reader, MemberReferenceHandle handle)
    {
        var member = reader.GetMemberReference(handle);
        if (member.Parent.Kind != HandleKind.TypeReference) return null;
        var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
        return $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
    }

    private static string GetMethodDefinitionAttributeType(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        var type = reader.GetTypeDefinition(method.GetDeclaringType());
        return $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
    }

    internal sealed record Requirement(string PackageId, string? MinimumVersion);
    internal sealed record PackageMetadata(string Id, ShiroBotPackageKind Kind, string Version);
}
