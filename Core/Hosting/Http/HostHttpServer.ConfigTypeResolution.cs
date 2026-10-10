using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ShiroBot.SDK.Config;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static Type? FindDeclaredConfigType(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
        foreach (var type in types.Where(type => type.IsVisible && !type.IsAbstract && !type.ContainsGenericParameters))
        {
            var declaration = type.GetInterfaces().FirstOrDefault(contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IConfigurableComponent<>));
            if (declaration is not null) return declaration.GetGenericArguments()[0];
        }
        return null;
    }

    // Disabled components are inspected without loading any assembly or invoking component code.
    private static (string Path, string Name)? FindDeclaredConfigMetadata(string entryPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(entryPath))!;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        using var stream = File.OpenRead(entryPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return null;
        var reader = pe.GetMetadataReader();
        var provider = new ConfigDeclarationProvider(reader);
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public ||
                (type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0 ||
                type.GetGenericParameters().Count != 0) continue;
            var found = Visit(provider.GetTypeFromDefinition(reader, handle, 0), [], 0);
            if (found is not null)
            {
                var path = ResolvePath(found.Assembly);
                if (path is not null) return (path, found.Name);
            }
        }
        return null;

        string? ResolvePath(string name)
        {
            if (name == provider.AssemblyName) return entryPath;
            // Assembly reference names must be simple filenames, never paths outside the package.
            if (name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\')) return null;
            var path = Path.Combine(directory, name + ".dll");
            return File.Exists(path) ? path : null;
        }

        ConfigDeclaration? Visit(ConfigDeclaration declaration, ImmutableArray<ConfigDeclaration> context, int depth)
        {
            if (declaration.Assembly == typeof(IConfigurableComponent<>).Assembly.GetName().Name &&
                declaration.Name is "ShiroBot.SDK.Config.IConfigurableComponent`1" or "ShiroBot.SDK.Plugin.PluginBase`1" &&
                declaration.Arguments.Length == 1)
                return declaration.Arguments[0];
            if (depth >= 32 || !visited.Add(declaration.ToString())) return null;
            var path = ResolvePath(declaration.Assembly);
            if (path is null) return null;
            using var typeStream = File.OpenRead(path);
            using var typePe = new PEReader(typeStream);
            if (!typePe.HasMetadata) return null;
            var typeReader = typePe.GetMetadataReader();
            var decoder = new ConfigDeclarationProvider(typeReader);
            foreach (var handle in typeReader.TypeDefinitions)
            {
                if (decoder.GetTypeFromDefinition(typeReader, handle, 0).Name != declaration.Name) continue;
                var type = typeReader.GetTypeDefinition(handle);
                var arguments = declaration.Arguments.IsEmpty ? context : declaration.Arguments;
                var parents = type.GetInterfaceImplementations()
                    .Select(item => typeReader.GetInterfaceImplementation(item).Interface).ToList();
                if (!type.BaseType.IsNil) parents.Add(type.BaseType);
                foreach (var parent in parents)
                {
                    var found = Visit(decoder.Decode(parent, arguments), arguments, depth + 1);
                    if (found is not null) return found;
                }
                break;
            }
            return null;
        }
    }

    private sealed record ConfigDeclaration(string Assembly, string Name, ImmutableArray<ConfigDeclaration> Arguments)
    {
        public override string ToString() => $"{Assembly}:{Name}<{string.Join(",", Arguments)}>";
    }

    private sealed class ConfigDeclarationProvider(MetadataReader metadata)
        : ISignatureTypeProvider<ConfigDeclaration, ImmutableArray<ConfigDeclaration>>
    {
        public string AssemblyName { get; } = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        public ConfigDeclaration Decode(EntityHandle handle, ImmutableArray<ConfigDeclaration> context) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(metadata, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(metadata, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(metadata, context, (TypeSpecificationHandle)handle, 0),
            _ => new("", "", [])
        };
        public ConfigDeclaration GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return new(AssemblyName, FullName(reader, type.Namespace, type.Name), []);
        }
        public ConfigDeclaration GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var assembly = type.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)
                : AssemblyName;
            return new(assembly, FullName(reader, type.Namespace, type.Name), []);
        }
        private static string FullName(MetadataReader reader, StringHandle ns, StringHandle name) =>
            string.IsNullOrEmpty(reader.GetString(ns)) ? reader.GetString(name) : reader.GetString(ns) + "." + reader.GetString(name);
        public ConfigDeclaration GetTypeFromSpecification(MetadataReader reader, ImmutableArray<ConfigDeclaration> context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        public ConfigDeclaration GetGenericInstantiation(ConfigDeclaration genericType, ImmutableArray<ConfigDeclaration> typeArguments) => genericType with { Arguments = typeArguments };
        public ConfigDeclaration GetGenericTypeParameter(ImmutableArray<ConfigDeclaration> context, int index) => index < context.Length ? context[index] : new("", "!" + index, []);
        public ConfigDeclaration GetGenericMethodParameter(ImmutableArray<ConfigDeclaration> context, int index) => new("", "!!" + index, []);
        public ConfigDeclaration GetPrimitiveType(PrimitiveTypeCode typeCode) => new("", typeCode.ToString(), []);
        public ConfigDeclaration GetArrayType(ConfigDeclaration elementType, ArrayShape shape) => elementType;
        public ConfigDeclaration GetByReferenceType(ConfigDeclaration elementType) => elementType;
        public ConfigDeclaration GetFunctionPointerType(MethodSignature<ConfigDeclaration> signature) => new("", "", []);
        public ConfigDeclaration GetModifiedType(ConfigDeclaration modifier, ConfigDeclaration unmodifiedType, bool isRequired) => unmodifiedType;
        public ConfigDeclaration GetPinnedType(ConfigDeclaration elementType) => elementType;
        public ConfigDeclaration GetPointerType(ConfigDeclaration elementType) => elementType;
        public ConfigDeclaration GetSZArrayType(ConfigDeclaration elementType) => elementType;
    }
}
