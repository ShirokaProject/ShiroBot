using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length is < 2 or > 3)
    throw new ArgumentException("Usage: VersionCheck <assembly.dll> <project-version> [tag-version]");
using var file = File.OpenRead(args[0]);
using var pe = new PEReader(file);
var reader = pe.GetMetadataReader();
var provider = new AttributeTypes();
string? packageVersion = null;
var componentVersions = new List<string>();
foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
{
    var attribute = reader.GetCustomAttribute(handle);
    if (AttributeName(attribute) == "AssemblyInformationalVersionAttribute")
        packageVersion = (string?)attribute.DecodeValue(provider).FixedArguments[0].Value;
}
foreach (var handle in reader.TypeDefinitions)
    foreach (var attributeHandle in reader.GetTypeDefinition(handle).GetCustomAttributes())
    {
        var attribute = reader.GetCustomAttribute(attributeHandle);
        if (AttributeName(attribute) != "BotPluginAttribute") continue;
        var version = attribute.DecodeValue(provider).NamedArguments.SingleOrDefault(argument => argument.Name == "Version");
        if (version.Value is not string text || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Component metadata has no version.");
        componentVersions.Add(text);
    }
if (componentVersions.Count != 1)
    throw new InvalidOperationException($"Expected one BotPluginAttribute, found {componentVersions.Count}.");
// The SDK appends the source revision to InformationalVersion; it is not the release version.
var assemblyVersion = packageVersion?.Split('+')[0];
var projectVersion = args[1];
var tagVersion = args.Length == 3 ? args[2] : projectVersion;
if (assemblyVersion != projectVersion || componentVersions[0] != projectVersion || tagVersion != projectVersion)
    throw new InvalidOperationException($"Version mismatch: metadata={componentVersions[0]}, assembly={assemblyVersion}, project={projectVersion}, tag={tagVersion}.");
Console.WriteLine($"Verified component metadata, assembly, project and release version: {projectVersion}");

string AttributeName(CustomAttribute attribute)
{
    EntityHandle type = attribute.Constructor.Kind switch
    {
        HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
        HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
        _ => throw new BadImageFormatException("Unsupported attribute constructor.")
    };
    return type.Kind switch
    {
        HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)type).Name),
        HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)type).Name),
        _ => throw new BadImageFormatException("Unsupported attribute type.")
    };
}

sealed class AttributeTypes : ICustomAttributeTypeProvider<string>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSystemType() => "System.Type";
    public bool IsSystemType(string type) => type == "System.Type";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromSerializedName(string name) => name;
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => reader.GetString(reader.GetTypeReference(handle).Name);
    // SDK component categories are int-backed enums. No component code is loaded or executed.
    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
}
