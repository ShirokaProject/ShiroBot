using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var options = GeneratorOptions.Parse(args);
if (options.SelfTest)
{
    RunCompatibilitySelfTest();
    return;
}

var compatibility = LoadCompatibilityCatalog(options.OutputDirectory);

using var http = new HttpClient();
var json = await http.GetStringAsync(options.IrUrl);
var irSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
if (!string.IsNullOrWhiteSpace(options.ExpectedSha256) &&
    !string.Equals(options.ExpectedSha256, irSha256, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Milky IR SHA-256 mismatch. Expected {options.ExpectedSha256}, received {irSha256}. " +
        "Review the upstream ABI change and update the lock explicitly.");
}

var ir = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("Failed to parse ir.json.");
var sourceInfo = new IrSourceInfo(
    options.IrSourceName,
    options.IrUrl,
    irSha256,
    ir["milkyVersion"]?.GetValue<string>() ?? string.Empty,
    ir["milkyPackageVersion"]?.GetValue<string>() ?? string.Empty);

var stagingDirectory = options.OutputDirectory + ".staging-" + Guid.NewGuid().ToString("N");
Directory.CreateDirectory(stagingDirectory);

try
{
    GenerateHeader(stagingDirectory, options, sourceInfo);

    var commonStructs = ir["commonStructs"]?.AsArray() ?? [];
    var generationContext = BuildGenerationContext(commonStructs);
    var eventMetadataDefinitions = CollectEventMetadataDefinitions(commonStructs, generationContext);
    var eventTypeDefinitions = CollectDiscriminatorTypeDefinitions(commonStructs, "Event");
    var incomingMessageTypeDefinitions = CollectDiscriminatorTypeDefinitions(commonStructs, "IncomingMessage");

    GenerateCommonStructs(stagingDirectory, commonStructs, generationContext, options, compatibility);
    GenerateEventMetadataRegistry(
        stagingDirectory,
        eventMetadataDefinitions,
        eventTypeDefinitions,
        incomingMessageTypeDefinitions,
        options);
    GenerateApiTypes(stagingDirectory, ir["apiCategories"]?.AsArray() ?? [], options, compatibility);
    ValidateCompatibilityCatalog(compatibility, stagingDirectory);
    CommitGeneratedDirectory(stagingDirectory, options.OutputDirectory);
}
finally
{
    if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
}

Console.WriteLine($"Generated model files into: {options.OutputDirectory}");

static void CommitGeneratedDirectory(string stagingDirectory, string outputDirectory)
{
    var backupDirectory = outputDirectory + ".backup-" + Guid.NewGuid().ToString("N");
    var hadExistingOutput = Directory.Exists(outputDirectory);
    try
    {
        if (hadExistingOutput) Directory.Move(outputDirectory, backupDirectory);
        Directory.Move(stagingDirectory, outputDirectory);
        if (hadExistingOutput) Directory.Delete(backupDirectory, recursive: true);
    }
    catch
    {
        if (!Directory.Exists(outputDirectory) && Directory.Exists(backupDirectory))
        {
            Directory.Move(backupDirectory, outputDirectory);
        }

        throw;
    }
}

static ModelCompatibilityCatalog LoadCompatibilityCatalog(string generatedDir)
{
    var records = new Dictionary<string, ExistingRecordAbi>(StringComparer.Ordinal);
    if (!Directory.Exists(generatedDir)) return new ModelCompatibilityCatalog(records, new HashSet<string>(StringComparer.Ordinal));
    var publicTypes = LoadPublicTypeNames(generatedDir);

    foreach (var path in Directory.EnumerateFiles(generatedDir, "*.cs", SearchOption.AllDirectories))
    {
        if (Path.GetFileName(path).Equals("EventMetadataRegistry.cs", StringComparison.OrdinalIgnoreCase)) continue;

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        foreach (var declaration in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
        {
            if (declaration.ParameterList is null) continue;

            var namespaceName = string.Join('.', declaration.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(item => item.Name.ToString()));
            var fullName = string.IsNullOrWhiteSpace(namespaceName)
                ? declaration.Identifier.ValueText
                : $"{namespaceName}.{declaration.Identifier.ValueText}";
            var primary = ReadParameters(declaration.ParameterList.Parameters);
            var constructors = new List<ExistingAbiMember> { new(primary) };
            constructors.AddRange(declaration.Members
                .OfType<ConstructorDeclarationSyntax>()
                .Where(constructor => constructor.Modifiers.Any(SyntaxKind.PublicKeyword))
                .Select(constructor => new ExistingAbiMember(ReadParameters(constructor.ParameterList.Parameters))));

            var deconstructors = new List<ExistingAbiMember> { new(primary) };
            deconstructors.AddRange(declaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "Deconstruct" &&
                                 method.Modifiers.Any(SyntaxKind.PublicKeyword))
                .Select(method => new ExistingAbiMember(ReadParameters(method.ParameterList.Parameters))));

            if (!records.TryAdd(fullName, new ExistingRecordAbi(
                    primary,
                    DistinctAbiMembers(constructors),
                    DistinctAbiMembers(deconstructors))))
            {
                throw new InvalidOperationException($"Multiple positional records named {fullName} were found in Generated.");
            }
        }
    }

    return new ModelCompatibilityCatalog(records, publicTypes);
}

static HashSet<string> LoadPublicTypeNames(string generatedDir)
{
    var result = new HashSet<string>(StringComparer.Ordinal);
    foreach (var path in Directory.EnumerateFiles(generatedDir, "*.cs", SearchOption.AllDirectories))
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                     .Where(declaration => declaration.Modifiers.Any(SyntaxKind.PublicKeyword)))
        {
            var namespaceName = string.Join('.', declaration.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(item => item.Name.ToString()));
            var fullName = string.IsNullOrWhiteSpace(namespaceName)
                ? declaration.Identifier.ValueText
                : $"{namespaceName}.{declaration.Identifier.ValueText}";
            result.Add(fullName);
        }
    }

    return result;
}

static void ValidateCompatibilityCatalog(ModelCompatibilityCatalog catalog, string generatedDir)
{
    var generatedTypes = LoadPublicTypeNames(generatedDir);
    var missingTypes = catalog.PublicTypes
        .Where(typeName => !generatedTypes.Contains(typeName))
        .OrderBy(typeName => typeName, StringComparer.Ordinal)
        .ToArray();
    if (missingTypes.Length > 0)
    {
        throw new InvalidOperationException(
            "Model ABI break: previously generated public types were removed or renamed: " +
            string.Join(", ", missingTypes) + ". A major breaking change is required.");
    }
}

static IReadOnlyList<ExistingModelParameter> ReadParameters(SeparatedSyntaxList<ParameterSyntax> parameters) =>
    parameters.Select(parameter => new ExistingModelParameter(
            parameter.Identifier.ValueText,
            NormalizeTypeName(parameter.Type?.ToString() ??
                              throw new InvalidOperationException("Generated record parameter has no type."))))
        .ToArray();

static IReadOnlyList<ExistingAbiMember> DistinctAbiMembers(IEnumerable<ExistingAbiMember> members)
{
    var result = new List<ExistingAbiMember>();
    var signatures = new HashSet<string>(StringComparer.Ordinal);
    foreach (var member in members)
    {
        if (signatures.Add(GetRuntimeSignature(member.Parameters))) result.Add(member);
    }

    return result;
}

static CompatibilityPlan BuildCompatibilityPlan(
    string recordFullName,
    IReadOnlyList<GeneratedModelParameter> currentParameters,
    ModelCompatibilityCatalog catalog)
{
    if (!catalog.Records.TryGetValue(recordFullName, out var existing))
    {
        return CompatibilityPlan.Empty;
    }

    var currentByName = currentParameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
    foreach (var oldParameter in existing.PrimaryParameters)
    {
        if (!currentByName.TryGetValue(oldParameter.Name, out var current))
        {
            throw new InvalidOperationException(
                $"Model ABI break in {recordFullName}: positional field {oldParameter.Name} was removed or renamed. " +
                "A major breaking change is required.");
        }

        if (!string.Equals(oldParameter.Type, current.Type, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model ABI break in {recordFullName}.{oldParameter.Name}: type changed from " +
                $"{oldParameter.Type} to {current.Type}. A major breaking change is required.");
        }
    }

    var retainedOrder = currentParameters
        .Where(parameter => existing.PrimaryParameters.Any(old =>
            string.Equals(old.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
        .Select(parameter => parameter.Name)
        .ToArray();
    if (!retainedOrder.SequenceEqual(
            existing.PrimaryParameters.Select(parameter => parameter.Name),
            StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Model ABI break in {recordFullName}: existing positional fields were reordered. " +
            "A major breaking change is required.");
    }

    var generatedConstructors = new List<IReadOnlyList<GeneratedModelParameter>> { currentParameters };
    var optionalStart = currentParameters
        .Select((parameter, index) => new { parameter, index })
        .FirstOrDefault(item => IsCtorOptional(item.parameter.Field))?.index ?? -1;
    if (optionalStart >= 0)
    {
        for (var count = optionalStart; count < currentParameters.Count; count++)
        {
            generatedConstructors.Add(currentParameters.Take(count).ToArray());
        }
    }

    var constructors = BuildMissingAbiMembers(
        recordFullName,
        "constructor",
        existing.Constructors,
        generatedConstructors,
        currentByName);
    var deconstructors = BuildMissingAbiMembers(
        recordFullName,
        "Deconstruct",
        existing.Deconstructors,
        [currentParameters],
        currentByName);
    return new CompatibilityPlan(constructors, deconstructors);
}

static IReadOnlyList<CompatibilityAbiMember> BuildMissingAbiMembers(
    string recordFullName,
    string memberKind,
    IReadOnlyList<ExistingAbiMember> existingMembers,
    IReadOnlyList<IReadOnlyList<GeneratedModelParameter>> generatedMembers,
    IReadOnlyDictionary<string, GeneratedModelParameter> currentByName)
{
    var result = new List<CompatibilityAbiMember>();
    foreach (var oldMember in existingMembers)
    {
        var propertyNames = new List<string>(oldMember.Parameters.Count);
        foreach (var oldParameter in oldMember.Parameters)
        {
            if (!currentByName.TryGetValue(oldParameter.Name, out var current))
            {
                throw new InvalidOperationException(
                    $"Model ABI break in {recordFullName}: {memberKind} parameter {oldParameter.Name} " +
                    "was removed or renamed. A major breaking change is required.");
            }

            if (!string.Equals(oldParameter.Type, current.Type, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Model ABI break in {recordFullName}.{oldParameter.Name}: {memberKind} parameter type changed " +
                    $"from {oldParameter.Type} to {current.Type}. A major breaking change is required.");
            }

            propertyNames.Add(current.Name);
        }

        var oldSignature = GetRuntimeSignature(oldMember.Parameters);
        var generatedMatch = generatedMembers.FirstOrDefault(member =>
            string.Equals(GetGeneratedRuntimeSignature(member), oldSignature, StringComparison.Ordinal));
        if (generatedMatch is not null)
        {
            if (!generatedMatch.Select(parameter => parameter.Name)
                    .SequenceEqual(propertyNames, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Model ABI break in {recordFullName}: the generated {memberKind} signature collides with " +
                    "an older overload but maps parameters to different fields. A major breaking change is required.");
            }

            continue;
        }

        var plannedMatch = result.FirstOrDefault(member =>
            string.Equals(GetRuntimeSignature(member.Parameters), oldSignature, StringComparison.Ordinal));
        if (plannedMatch is not null)
        {
            if (!plannedMatch.PropertyNames.SequenceEqual(propertyNames, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Model ABI break in {recordFullName}: older {memberKind} overloads have colliding signatures.");
            }

            continue;
        }

        result.Add(new CompatibilityAbiMember(oldMember.Parameters, propertyNames));
    }

    return result;
}

static string GetRuntimeSignature(IEnumerable<ExistingModelParameter> parameters) =>
    string.Join('|', parameters.Select(parameter => GetRuntimeTypeSignature(parameter.Type)));

static string GetGeneratedRuntimeSignature(IEnumerable<GeneratedModelParameter> parameters) =>
    string.Join('|', parameters.Select(parameter => GetRuntimeTypeSignature(parameter.Type)));

static string GetRuntimeTypeSignature(string typeName) => typeName.Replace("?", string.Empty, StringComparison.Ordinal);

static string NormalizeTypeName(string typeName) =>
    SyntaxFactory.ParseTypeName(typeName).NormalizeWhitespace().ToFullString();

static void RunCompatibilitySelfTest()
{
    var root = Path.Combine(Path.GetTempPath(), "MilkyModelGenerator.Net", Guid.NewGuid().ToString("N"));
    var generated = Path.Combine(root, "Generated");
    var common = Path.Combine(generated, "Common");
    var requests = Path.Combine(generated, "System", "Requests");
    var responses = Path.Combine(generated, "System", "Responses");
    Directory.CreateDirectory(common);
    Directory.CreateDirectory(requests);
    Directory.CreateDirectory(responses);

    try
    {
        File.WriteAllText(Path.Combine(common, "Fixture.cs"), """
            // <auto-generated />
            #nullable enable
            namespace Fixture.Models.Common;

            public sealed partial record Fixture(string Name);
            """);
        File.WriteAllText(Path.Combine(requests, "ProbeRequest.cs"), """
            // <auto-generated />
            #nullable enable
            namespace Fixture.Models.System.Requests;

            public sealed partial record ProbeRequest(string Value);
            """);
        File.WriteAllText(Path.Combine(responses, "ProbeResponse.cs"), """
            // <auto-generated />
            #nullable enable
            namespace Fixture.Models.System.Responses;

            public sealed partial record ProbeResponse(int Result);
            """);

        var catalog = LoadCompatibilityCatalog(generated);
        var fields = JsonNode.Parse("""
            [
              { "fieldType": "scalar", "name": "name", "isArray": false, "isOptional": false, "scalarType": "string" },
              { "fieldType": "scalar", "name": "count", "isArray": false, "isOptional": false, "scalarType": "int32" }
            ]
            """)!.AsArray();
        var options = new GeneratorOptions(
            "https://example.invalid/ir.json",
            "fixture",
            generated,
            "Fixture.Models",
            null,
            false);
        GenerateSimpleStruct(generated, "Common", "Fixture", fields, [], options, catalog);

        var output = File.ReadAllText(Path.Combine(common, "Fixture.cs"));
        if (!output.Contains("[method: JsonConstructor]", StringComparison.Ordinal) ||
            !output.Contains(": this(Name, default!)", StringComparison.Ordinal) ||
            !output.Contains("public void Deconstruct(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Compatibility fixture did not emit the old constructor and Deconstruct overload.");
        }

        var apiCategories = JsonNode.Parse("""
            [
              {
                "name": "系统 API",
                "apis": [
                  {
                    "endpoint": "probe",
                    "requestFields": [
                      { "fieldType": "scalar", "name": "value", "isArray": false, "isOptional": false, "scalarType": "string" },
                      { "fieldType": "scalar", "name": "sequence", "isArray": false, "isOptional": false, "scalarType": "int64" }
                    ],
                    "responseFields": [
                      { "fieldType": "scalar", "name": "result", "isArray": false, "isOptional": false, "scalarType": "int32" },
                      { "fieldType": "scalar", "name": "message", "isArray": false, "isOptional": false, "scalarType": "string" }
                    ]
                  }
                ]
              }
            ]
            """)!.AsArray();
        GenerateApiTypes(generated, apiCategories, options, catalog);

        var requestOutput = File.ReadAllText(Path.Combine(requests, "ProbeRequest.cs"));
        var responseOutput = File.ReadAllText(Path.Combine(responses, "ProbeResponse.cs"));
        if (!requestOutput.Contains("[method: JsonConstructor]", StringComparison.Ordinal) ||
            !requestOutput.Contains(": this(Value, default!)", StringComparison.Ordinal) ||
            !requestOutput.Contains("public void Deconstruct(", StringComparison.Ordinal) ||
            !responseOutput.Contains("[method: JsonConstructor]", StringComparison.Ordinal) ||
            !responseOutput.Contains(": this(Result, default!)", StringComparison.Ordinal) ||
            !responseOutput.Contains("public void Deconstruct(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "API compatibility fixture did not preserve old request/response constructors and Deconstruct overloads.");
        }

        ValidateCompatibilityFixtureAssembly(generated);

        var breakingFields = JsonNode.Parse("""
            [
              { "fieldType": "scalar", "name": "count", "isArray": false, "isOptional": false, "scalarType": "int32" }
            ]
            """)!.AsArray();
        try
        {
            GenerateSimpleStruct(generated, "Common", "Fixture", breakingFields, [], options, catalog);
            throw new InvalidOperationException("Compatibility fixture accepted a removed positional field.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("removed or renamed", StringComparison.Ordinal))
        {
        }

        Console.WriteLine("Generator compatibility self-test passed.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void ValidateCompatibilityFixtureAssembly(string generated)
{
    var syntaxTrees = Directory.EnumerateFiles(generated, "*.cs", SearchOption.AllDirectories)
        .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
        .ToArray();
    var trustedAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
    var references = trustedAssemblies
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();
    var compilation = CSharpCompilation.Create(
        $"MilkyModelGeneratorCompatibilityFixture_{Guid.NewGuid():N}",
        syntaxTrees,
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    using var assemblyStream = new MemoryStream();
    var emitResult = compilation.Emit(assemblyStream);
    if (!emitResult.Success)
    {
        throw new InvalidOperationException(
            "Compatibility fixture failed to compile: " +
            string.Join(Environment.NewLine, emitResult.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
    }

    var assembly = Assembly.Load(assemblyStream.ToArray());
    var requestType = assembly.GetType("Fixture.Models.System.Requests.ProbeRequest", throwOnError: true)!;
    var request = JsonSerializer.Deserialize("{\"Value\":\"test\",\"Sequence\":42}", requestType)
                  ?? throw new InvalidOperationException("Compatibility fixture request deserialized to null.");
    if (!string.Equals(requestType.GetProperty("Value")?.GetValue(request) as string, "test", StringComparison.Ordinal) ||
        !Equals(requestType.GetProperty("Sequence")?.GetValue(request), 42L))
    {
        throw new InvalidOperationException("Compatibility fixture request did not deserialize through the primary constructor.");
    }
}

static void GenerateHeader(string generatedDir, GeneratorOptions options, IrSourceInfo source)
{
    var path = Path.Combine(generatedDir, "_GeneratedInfo.cs");
    WriteFile(path, $$"""
// <auto-generated />
#nullable enable
namespace {{options.GeneratedRootNamespace}};

internal static class GeneratedInfo
{
    public const string Source = {{ToCSharpStringLiteral(source.Name)}};
    public const string Url = {{ToCSharpStringLiteral(source.Url)}};
    public const string Sha256 = {{ToCSharpStringLiteral(source.Sha256)}};
    public const string MilkyVersion = {{ToCSharpStringLiteral(source.MilkyVersion)}};
    public const string MilkyPackageVersion = {{ToCSharpStringLiteral(source.MilkyPackageVersion)}};
}
""");
}

static GenerationContext BuildGenerationContext(JsonArray commonStructs)
{
    var unionMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    var referencedInterfaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    foreach (var item in commonStructs.OfType<JsonObject>())
    {
        var name = item["name"]?.GetValue<string>();
        var structType = item["structType"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name) || structType != "union")
        {
            continue;
        }

        var members = new HashSet<string>(StringComparer.Ordinal);
        unionMembers[name] = members;

        if (item["unionType"]?.GetValue<string>() == "plain")
        {
            foreach (var derived in item["derivedStructs"]?.AsArray()?.OfType<JsonObject>() ?? [])
            {
                var tagValue = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(tagValue))
                {
                    members.Add($"{SnakeToPascal(tagValue)}{name}");
                }
            }
        }
        else
        {
            foreach (var derived in item["derivedTypes"]?.AsArray()?.OfType<JsonObject>() ?? [])
            {
                var tagValue = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
                var derivingType = derived["derivingType"]?.GetValue<string>() ?? "struct";
                if (derivingType == "struct" && !string.IsNullOrWhiteSpace(tagValue))
                {
                    members.Add($"{SnakeToPascal(tagValue)}{name}");
                }
            }
        }
    }

    foreach (var item in commonStructs.OfType<JsonObject>())
    {
        var name = item["name"]?.GetValue<string>();
        var structType = item["structType"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name) || structType != "union" || item["unionType"]?.GetValue<string>() != "withData")
        {
            continue;
        }

        foreach (var derived in item["derivedTypes"]?.AsArray()?.OfType<JsonObject>() ?? [])
        {
            if (derived["derivingType"]?.GetValue<string>() != "ref")
            {
                continue;
            }

            var refStructName = derived["refStructName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(refStructName))
            {
                continue;
            }

            if (unionMembers.TryGetValue(refStructName, out var memberNames))
            {
                foreach (var memberName in memberNames)
                {
                    AddInterface(referencedInterfaces, memberName, name);
                }
            }
            else
            {
                AddInterface(referencedInterfaces, refStructName, name);
            }
        }
    }

    return new GenerationContext(unionMembers, referencedInterfaces);
}

static void AddInterface(
    IDictionary<string, HashSet<string>> interfaceMap,
    string typeName,
    string interfaceName)
{
    if (!interfaceMap.TryGetValue(typeName, out var interfaces))
    {
        interfaces = new HashSet<string>(StringComparer.Ordinal);
        interfaceMap[typeName] = interfaces;
    }

    interfaces.Add(interfaceName);
}

static void GenerateCommonStructs(
    string generatedDir,
    JsonArray commonStructs,
    GenerationContext context,
    GeneratorOptions options,
    ModelCompatibilityCatalog compatibility)
{
    foreach (var item in commonStructs.OfType<JsonObject>())
    {
        var structType = item["structType"]?.GetValue<string>();
        var name = item["name"]?.GetValue<string>() ?? "Unknown";

        if (structType == "simple")
        {
            GenerateSimpleStruct(generatedDir, "Common", name, item["fields"]?.AsArray() ?? [], [], options, compatibility);
            continue;
        }

        if (structType == "union")
        {
            GenerateUnionStruct(generatedDir, name, item, context, options, compatibility);
        }
    }
}

static IReadOnlyList<EventMetadataDefinition> CollectEventMetadataDefinitions(
    JsonArray commonStructs,
    GenerationContext context)
{
    var result = new List<EventMetadataDefinition>();

    foreach (var item in commonStructs.OfType<JsonObject>())
    {
        var structType = item["structType"]?.GetValue<string>();
        var name = item["name"]?.GetValue<string>() ?? "Unknown";
        if (structType != "union")
        {
            continue;
        }

        var unionType = item["unionType"]?.GetValue<string>() ?? "plain";
        var baseFields = item["baseFields"]?.AsArray() ?? [];
        if (unionType == "plain")
        {
            foreach (var derived in item["derivedStructs"]?.AsArray()?.OfType<JsonObject>() ?? [])
            {
                var tagValue = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(tagValue))
                {
                    continue;
                }

                var derivedName = $"{SnakeToPascal(tagValue)}{name}";
                var interfaces = GetInterfaces(context, derivedName, name);
                if (!interfaces.Contains("Event", StringComparer.Ordinal))
                {
                    continue;
                }

                result.Add(new EventMetadataDefinition(
                    derivedName,
                    derived["description"]?.GetValue<string>() ?? derivedName,
                    CollectFieldMetadataDefinitions(derived["fields"]?.AsArray() ?? [])));
            }

            continue;
        }

        foreach (var derived in item["derivedTypes"]?.AsArray()?.OfType<JsonObject>() ?? [])
        {
            if (derived["derivingType"]?.GetValue<string>() != "struct")
            {
                continue;
            }

            var tagValue = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tagValue))
            {
                continue;
            }

            var derivedName = $"{SnakeToPascal(tagValue)}{name}";
            var interfaces = GetInterfaces(context, derivedName, name);
            if (!interfaces.Contains("Event", StringComparer.Ordinal))
            {
                continue;
            }

            var combinedFields = new JsonArray();
            foreach (var field in baseFields)
            {
                combinedFields.Add(field?.DeepClone());
            }

            foreach (var field in derived["fields"]?.AsArray() ?? [])
            {
                combinedFields.Add(field?.DeepClone());
            }

            result.Add(new EventMetadataDefinition(
                derivedName,
                derived["description"]?.GetValue<string>() ?? derivedName,
                CollectFieldMetadataDefinitions(combinedFields)));
        }
    }

    return result
        .GroupBy(item => item.TypeName, StringComparer.Ordinal)
        .Select(group => group.Last())
        .OrderBy(item => item.TypeName, StringComparer.Ordinal)
        .ToArray();
}

static IReadOnlyList<EventFieldMetadataDefinition> CollectFieldMetadataDefinitions(JsonArray fields)
{
    return OrderFieldsForCtor(fields)
        .Select(field => new EventFieldMetadataDefinition(
            SnakeToPascal(field["name"]?.GetValue<string>() ?? "Unknown"),
            field["description"]?.GetValue<string>() ?? string.Empty))
        .Where(field => !string.IsNullOrWhiteSpace(field.Description))
        .ToArray();
}

static IReadOnlyList<DiscriminatorTypeDefinition> CollectDiscriminatorTypeDefinitions(
    JsonArray commonStructs,
    string unionName)
{
    var union = commonStructs
        .OfType<JsonObject>()
        .FirstOrDefault(item =>
            item["structType"]?.GetValue<string>() == "union" &&
            string.Equals(item["name"]?.GetValue<string>(), unionName, StringComparison.Ordinal));
    if (union is null)
    {
        return [];
    }

    var definitions = new List<DiscriminatorTypeDefinition>();
    if (union["unionType"]?.GetValue<string>() == "plain")
    {
        foreach (var derived in union["derivedStructs"]?.AsArray()?.OfType<JsonObject>() ?? [])
        {
            var discriminator = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(discriminator))
            {
                definitions.Add(new(discriminator, $"{SnakeToPascal(discriminator)}{unionName}"));
            }
        }
    }
    else
    {
        foreach (var derived in union["derivedTypes"]?.AsArray()?.OfType<JsonObject>() ?? [])
        {
            var discriminator = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
            var typeName = derived["derivingType"]?.GetValue<string>() switch
            {
                "struct" => $"{SnakeToPascal(discriminator)}{unionName}",
                "ref" => derived["refStructName"]?.GetValue<string>(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(discriminator) && !string.IsNullOrWhiteSpace(typeName))
            {
                definitions.Add(new(discriminator, typeName));
            }
        }
    }

    return definitions
        .GroupBy(item => item.Discriminator, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .OrderBy(item => item.Discriminator, StringComparer.Ordinal)
        .ToArray();
}

static void GenerateEventMetadataRegistry(
    string generatedDir,
    IReadOnlyList<EventMetadataDefinition> definitions,
    IReadOnlyList<DiscriminatorTypeDefinition> eventTypes,
    IReadOnlyList<DiscriminatorTypeDefinition> incomingMessageTypes,
    GeneratorOptions options)
{
    var ns = GetNamespace("Common", options);
    var targetDir = GetOutputDirectory(generatedDir, "Common");
    Directory.CreateDirectory(targetDir);

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine();
    sb.AppendLine($"namespace {ns};");
    sb.AppendLine();
    sb.AppendLine("public sealed record EventFieldMetadata(string PropertyName, string Description);");
    sb.AppendLine();
    sb.AppendLine("public sealed record EventMetadata(string Description, IReadOnlyList<EventFieldMetadata> Fields);");
    sb.AppendLine();
    sb.AppendLine("public static class EventMetadataRegistry");
    sb.AppendLine("{");
    sb.AppendLine("    private static readonly IReadOnlyDictionary<Type, EventMetadata> Metadata = new Dictionary<Type, EventMetadata>");
    sb.AppendLine("    {");

    for (var i = 0; i < definitions.Count; i++)
    {
        var definition = definitions[i];
        var suffix = i == definitions.Count - 1 ? string.Empty : ",";
        sb.AppendLine($"        [typeof({definition.TypeName})] = new({ToCSharpStringLiteral(definition.Description)},");
        sb.AppendLine("        [");

        for (var j = 0; j < definition.Fields.Count; j++)
        {
            var field = definition.Fields[j];
            var fieldSuffix = j == definition.Fields.Count - 1 ? string.Empty : ",";
            sb.AppendLine(
                $"            new(nameof({definition.TypeName}.{field.PropertyName}), {ToCSharpStringLiteral(field.Description)}){fieldSuffix}");
        }

        sb.AppendLine($"        ]){suffix}");
    }

    sb.AppendLine("    };");
    sb.AppendLine();
    RenderDiscriminatorTypeMap(sb, "EventTypes", eventTypes);
    sb.AppendLine();
    RenderDiscriminatorTypeMap(sb, "IncomingMessageTypes", incomingMessageTypes);
    sb.AppendLine();
    sb.AppendLine("    public static bool TryGet(Type eventType, out EventMetadata metadata) =>");
    sb.AppendLine("        Metadata.TryGetValue(eventType, out metadata!);");
    sb.AppendLine();
    sb.AppendLine("    public static bool TryGetEventType(string discriminator, out Type eventType) =>");
    sb.AppendLine("        EventTypes.TryGetValue(discriminator, out eventType!);");
    sb.AppendLine();
    sb.AppendLine("    public static bool TryGetIncomingMessageType(string scene, out Type messageType) =>");
    sb.AppendLine("        IncomingMessageTypes.TryGetValue(scene, out messageType!);");
    sb.AppendLine("}");

    WriteFile(Path.Combine(targetDir, "EventMetadataRegistry.cs"), sb.ToString());
}

static void RenderDiscriminatorTypeMap(
    StringBuilder sb,
    string fieldName,
    IReadOnlyList<DiscriminatorTypeDefinition> definitions)
{
    sb.AppendLine(
        $"    private static readonly IReadOnlyDictionary<string, Type> {fieldName} = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)");
    sb.AppendLine("    {");
    for (var i = 0; i < definitions.Count; i++)
    {
        var definition = definitions[i];
        var suffix = i == definitions.Count - 1 ? string.Empty : ",";
        sb.AppendLine(
            $"        [{ToCSharpStringLiteral(definition.Discriminator)}] = typeof({definition.TypeName}){suffix}");
    }

    sb.AppendLine("    };");
}

static void GenerateApiTypes(
    string generatedDir,
    JsonArray categories,
    GeneratorOptions options,
    ModelCompatibilityCatalog compatibility)
{
    foreach (var category in categories.OfType<JsonObject>())
    {
        var categoryArea = MapApiCategoryArea(category["name"]?.GetValue<string>() ?? string.Empty);
        foreach (var api in category["apis"]?.AsArray()?.OfType<JsonObject>() ?? [])
        {
            var endpoint = api["endpoint"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                continue;
            }

            var baseName = SnakeToPascal(endpoint);
            if (api["requestFields"] is JsonArray requestFields)
            {
                GenerateSimpleStruct(
                    generatedDir,
                    $"{categoryArea}.Requests",
                    $"{baseName}Request",
                    requestFields,
                    [],
                    options,
                    compatibility);
            }

            if (api["responseFields"] is JsonArray responseFields)
            {
                GenerateSimpleStruct(
                    generatedDir,
                    $"{categoryArea}.Responses",
                    $"{baseName}Response",
                    responseFields,
                    [],
                    options,
                    compatibility);
            }
        }
    }
}

static void GenerateUnionStruct(
    string generatedDir,
    string name,
    JsonObject obj,
    GenerationContext context,
    GeneratorOptions options,
    ModelCompatibilityCatalog compatibility)
{
    const string area = "Common";
    var ns = GetNamespace(area, options);
    var targetDir = GetOutputDirectory(generatedDir, area);
    Directory.CreateDirectory(targetDir);

    var interfacePath = Path.Combine(targetDir, $"{name}.cs");
    WriteFile(interfacePath, $"// <auto-generated />\n#nullable enable\nnamespace {ns};\n\npublic interface {name}\n{{\n}}\n");

    var unionType = obj["unionType"]?.GetValue<string>() ?? "plain";
    if (unionType == "plain")
    {
        foreach (var derived in obj["derivedStructs"]?.AsArray()?.OfType<JsonObject>() ?? [])
        {
            var tagValue = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tagValue))
            {
                continue;
            }

            var derivedName = $"{SnakeToPascal(tagValue)}{name}";
            var interfaces = GetInterfaces(context, derivedName, name);
            GenerateSimpleStruct(
                generatedDir,
                area,
                derivedName,
                derived["fields"]?.AsArray() ?? [],
                interfaces,
                options,
                compatibility);
        }

        return;
    }

    var baseFields = obj["baseFields"]?.AsArray() ?? [];
    foreach (var derived in obj["derivedTypes"]?.AsArray()?.OfType<JsonObject>() ?? [])
    {
        var derivingType = derived["derivingType"]?.GetValue<string>() ?? "struct";
        if (derivingType != "struct")
        {
            continue;
        }

        var tagValue = derived["tagValue"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tagValue))
        {
            continue;
        }

        var derivedName = $"{SnakeToPascal(tagValue)}{name}";
        var fields = new JsonArray();
        foreach (var field in baseFields)
        {
            fields.Add(field?.DeepClone());
        }

        foreach (var field in derived["fields"]?.AsArray() ?? [])
        {
            fields.Add(field?.DeepClone());
        }

        var interfaces = GetInterfaces(context, derivedName, name);
        GenerateSimpleStruct(generatedDir, area, derivedName, fields, interfaces, options, compatibility);
    }
}

static IReadOnlyCollection<string> GetInterfaces(GenerationContext context, string typeName, string primaryInterface)
{
    var interfaces = new HashSet<string>(StringComparer.Ordinal) { primaryInterface };
    if (context.ReferencedInterfaces.TryGetValue(typeName, out var additional))
    {
        foreach (var name in additional)
        {
            interfaces.Add(name);
        }
    }

    return interfaces.OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

static void GenerateSimpleStruct(
    string generatedDir,
    string area,
    string name,
    JsonArray fields,
    IReadOnlyCollection<string> implementedInterfaces,
    GeneratorOptions options,
    ModelCompatibilityCatalog compatibility)
{
    var ns = GetNamespace(area, options);
    var targetDir = GetOutputDirectory(generatedDir, area);
    Directory.CreateDirectory(targetDir);

    var enumDefinitions = CollectEnumDefinitions(name, fields);
    foreach (var enumDefinition in enumDefinitions)
    {
        GenerateEnum(generatedDir, area, enumDefinition, options);
    }

    var orderedFields = OrderFieldsForCtor(fields);
    var currentParameters = orderedFields
        .Select(field => new GeneratedModelParameter(
            SnakeToPascal(field["name"]?.GetValue<string>() ?? "Unknown"),
            NormalizeTypeName(ResolveFieldType(name, field)),
            field))
        .ToArray();
    var compatibilityPlan = BuildCompatibilityPlan(
        $"{ns}.{name}",
        currentParameters,
        compatibility);
    var hasAdditionalConstructors = orderedFields.Any(IsCtorOptional) ||
                                    compatibilityPlan.Constructors.Count > 0;
    var members = orderedFields
        .Select(field => RenderCtorParameter(name, field))
        .ToList();

    var baseClause = implementedInterfaces.Count == 0
        ? string.Empty
        : $" : {string.Join(", ", implementedInterfaces)}";

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");

    var usingNamespaces = GetUsings(area, fields, options).ToHashSet(StringComparer.Ordinal);
    if (hasAdditionalConstructors)
    {
        usingNamespaces.Add("System.Text.Json.Serialization");
    }

    foreach (var usingNs in usingNamespaces.OrderBy(value => value, StringComparer.Ordinal))
    {
        sb.AppendLine($"using {usingNs};");
    }

    if (usingNamespaces.Count > 0)
    {
        sb.AppendLine();
    }

    sb.AppendLine($"namespace {ns};");
    sb.AppendLine();

    if (members.Count == 0)
    {
        sb.AppendLine($"public sealed partial record {name}{baseClause};");
    }
    else
    {
        var needsBody = baseClause.Length > 0 ||
                        hasAdditionalConstructors ||
                        compatibilityPlan.Deconstructors.Count > 0;
        if (hasAdditionalConstructors)
        {
            sb.AppendLine("[method: JsonConstructor]");
        }

        sb.AppendLine($"public sealed partial record {name}(");
        for (var i = 0; i < members.Count; i++)
        {
            var suffix = i == members.Count - 1
                ? needsBody ? ")" : $"){baseClause};"
                : ",";
            sb.AppendLine($"    {members[i]}{suffix}");
        }

        if (needsBody)
        {
            if (baseClause.Length > 0)
            {
                sb.AppendLine($"    {baseClause}");
            }

            sb.AppendLine("{");
            RenderOptionalConstructorOverloads(sb, name, orderedFields, indent: "    ");
            RenderCompatibilityMembers(sb, name, currentParameters, compatibilityPlan, indent: "    ");
            sb.AppendLine("}");
        }
    }

    WriteFile(Path.Combine(targetDir, $"{name}.cs"), sb.ToString());
}

static void RenderOptionalConstructorOverloads(StringBuilder sb, string ownerName, IReadOnlyList<JsonObject> orderedFields, string indent)
{
    var optionalStart = -1;
    for (var i = 0; i < orderedFields.Count; i++)
    {
        if (IsCtorOptional(orderedFields[i]))
        {
            optionalStart = i;
            break;
        }
    }

    if (optionalStart < 0)
    {
        return;
    }

    var fullArguments = orderedFields
        .Select(field => SnakeToPascal(field["name"]?.GetValue<string>() ?? "Unknown"))
        .ToArray();

    for (var count = optionalStart; count < orderedFields.Count; count++)
    {
        sb.AppendLine();
        sb.AppendLine($"{indent}public {ownerName}(");
        for (var i = 0; i < count; i++)
        {
            var suffix = i == count - 1 ? ")" : ",";
            sb.AppendLine($"{indent}    {RenderRequiredCtorParameter(ownerName, orderedFields[i])}{suffix}");
        }

        if (count == 0)
        {
            sb.AppendLine($"{indent}    )");
        }

        sb.AppendLine($"{indent}    : this({string.Join(", ", fullArguments.Select((argument, index) => index < count ? LowerFirst(argument) : RenderDefaultValue(ownerName, orderedFields[index]) ?? "default"))})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}}}");
    }
}

static void RenderCompatibilityMembers(
    StringBuilder sb,
    string ownerName,
    IReadOnlyList<GeneratedModelParameter> currentParameters,
    CompatibilityPlan compatibility,
    string indent)
{
    foreach (var constructor in compatibility.Constructors)
    {
        sb.AppendLine();
        sb.AppendLine($"{indent}public {ownerName}(");
        for (var i = 0; i < constructor.Parameters.Count; i++)
        {
            var parameter = constructor.Parameters[i];
            var suffix = i == constructor.Parameters.Count - 1 ? ")" : ",";
            sb.AppendLine($"{indent}    {parameter.Type} {parameter.Name}{suffix}");
        }

        if (constructor.Parameters.Count == 0)
        {
            sb.AppendLine($"{indent}    )");
        }

        var arguments = currentParameters.Select(current =>
        {
            var oldIndex = constructor.PropertyNames
                .Select((propertyName, index) => new { propertyName, index })
                .FirstOrDefault(item => string.Equals(item.propertyName, current.Name, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;
            return oldIndex >= 0
                ? constructor.Parameters[oldIndex].Name
                : RenderDefaultValue(ownerName, current.Field) ?? "default!";
        });
        sb.AppendLine($"{indent}    : this({string.Join(", ", arguments)})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}}}");
    }

    foreach (var deconstructor in compatibility.Deconstructors)
    {
        sb.AppendLine();
        sb.AppendLine($"{indent}public void Deconstruct(");
        for (var i = 0; i < deconstructor.Parameters.Count; i++)
        {
            var parameter = deconstructor.Parameters[i];
            var suffix = i == deconstructor.Parameters.Count - 1 ? ")" : ",";
            sb.AppendLine($"{indent}    out {parameter.Type} {parameter.Name}{suffix}");
        }

        if (deconstructor.Parameters.Count == 0)
        {
            sb.AppendLine($"{indent}    )");
        }

        sb.AppendLine($"{indent}{{");
        for (var i = 0; i < deconstructor.Parameters.Count; i++)
        {
            sb.AppendLine($"{indent}    {deconstructor.Parameters[i].Name} = this.{deconstructor.PropertyNames[i]};");
        }
        sb.AppendLine($"{indent}}}");
    }
}

static string RenderRequiredCtorParameter(string ownerName, JsonObject field)
{
    var type = ResolveFieldType(ownerName, field);
    var name = SnakeToPascal(field["name"]?.GetValue<string>() ?? "Unknown");
    return $"{type} {LowerFirst(name)}";
}

static string LowerFirst(string value)
{
    return string.IsNullOrEmpty(value)
        ? value
        : char.ToLowerInvariant(value[0]) + value[1..];
}

static IReadOnlyList<string> GetUsings(string area, JsonArray fields, GeneratorOptions options)
{
    var usings = new HashSet<string>(StringComparer.Ordinal);
    if (!string.Equals(area, "Common", StringComparison.Ordinal))
    {
        usings.Add(GetNamespace("Common", options));
    }

    foreach (var field in fields.OfType<JsonObject>())
    {
        if (field["isArray"]?.GetValue<bool>() == true)
        {
            usings.Add("System.Collections.Generic");
        }
    }

    return usings.OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

static IReadOnlyList<EnumDefinition> CollectEnumDefinitions(string ownerName, JsonArray fields)
{
    var result = new List<EnumDefinition>();
    foreach (var field in fields.OfType<JsonObject>())
    {
        if (field["fieldType"]?.GetValue<string>() != "enum")
        {
            continue;
        }

        var fieldName = field["name"]?.GetValue<string>() ?? "Unknown";
        var values = field["values"]?.AsArray()?.Select(x => x?.GetValue<string>() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray() ?? [];

        result.Add(new EnumDefinition($"{ownerName}{SnakeToPascal(fieldName)}", values));
    }

    return result;
}

static void GenerateEnum(string generatedDir, string area, EnumDefinition enumDefinition, GeneratorOptions options)
{
    var ns = GetNamespace(area, options);
    var targetDir = GetOutputDirectory(generatedDir, area);
    Directory.CreateDirectory(targetDir);

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine($"namespace {ns};");
    sb.AppendLine();
    sb.AppendLine($"public enum {enumDefinition.Name}");
    sb.AppendLine("{");

    var usedNames = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 0; i < enumDefinition.Values.Count; i++)
    {
        var name = MakeEnumMemberName(enumDefinition.Values[i], usedNames);
        var suffix = i == enumDefinition.Values.Count - 1 ? string.Empty : ",";
        sb.AppendLine($"    {name}{suffix}");
    }

    sb.AppendLine("}");
    WriteFile(Path.Combine(targetDir, $"{enumDefinition.Name}.cs"), sb.ToString());
}

static string MakeEnumMemberName(string value, ISet<string> usedNames)
{
    var candidate = SnakeToPascal(value);
    if (string.IsNullOrWhiteSpace(candidate))
    {
        candidate = "Unknown";
    }

    if (char.IsDigit(candidate[0]))
    {
        candidate = $"Value{candidate}";
    }

    var unique = candidate;
    var index = 2;
    while (!usedNames.Add(unique))
    {
        unique = $"{candidate}{index}";
        index++;
    }

    return unique;
}

static string RenderCtorParameter(string ownerName, JsonObject field)
{
    var type = ResolveFieldType(ownerName, field);
    var name = SnakeToPascal(field["name"]?.GetValue<string>() ?? "Unknown");
    var defaultValue = RenderDefaultValue(ownerName, field);
    return defaultValue is null
        ? $"{type} {name}"
        : $"{type} {name} = {defaultValue}";
}

static IReadOnlyList<JsonObject> OrderFieldsForCtor(JsonArray fields)
{
    return fields
        .OfType<JsonObject>()
        .Select((field, index) => new
        {
            Field = field,
            Index = index,
            IsOptional = IsCtorOptional(field)
        })
        .OrderBy(item => item.IsOptional)
        .ThenBy(item => item.Index)
        .Select(item => item.Field)
        .ToArray();
}

static bool IsCtorOptional(JsonObject field)
{
    if (field["isOptional"]?.GetValue<bool>() == true)
    {
        return true;
    }

    return field.ContainsKey("defaultValue");
}

static string? RenderDefaultValue(string ownerName, JsonObject field)
{
    if (!field.TryGetPropertyValue("defaultValue", out var defaultNode))
    {
        return field["isOptional"]?.GetValue<bool>() == true ? "null" : null;
    }

    if (defaultNode is null)
    {
        return "null";
    }

    var fieldType = field["fieldType"]?.GetValue<string>() ?? "scalar";
    var scalarType = field["scalarType"]?.GetValue<string>() ?? "string";
    var isArray = field["isArray"]?.GetValue<bool>() ?? false;

    if (isArray)
    {
        return defaultNode is JsonArray array && array.Count == 0 ? "[]" : null;
    }

    return fieldType switch
    {
        "scalar" => RenderScalarDefaultValue(defaultNode, scalarType),
        "enum" => RenderEnumDefaultValue(ownerName, field, defaultNode),
        "ref" => "null",
        _ => null
    };
}

static string? RenderScalarDefaultValue(JsonNode defaultNode, string scalarType)
{
    return scalarType switch
    {
        "bool" or "boolean" => defaultNode.GetValue<bool>() ? "true" : "false",
        "int32" => defaultNode.GetValue<int>().ToString(CultureInfo.InvariantCulture),
        "int64" => defaultNode.GetValue<long>().ToString(CultureInfo.InvariantCulture),
        "float32" => $"{defaultNode.GetValue<float>().ToString(CultureInfo.InvariantCulture)}f",
        "float64" => defaultNode.GetValue<double>().ToString(CultureInfo.InvariantCulture),
        "string" => ToCSharpStringLiteral(defaultNode.GetValue<string>()),
        _ => null
    };
}

static string? RenderEnumDefaultValue(string ownerName, JsonObject field, JsonNode defaultNode)
{
    if (defaultNode is not JsonValue)
    {
        return null;
    }

    var enumTypeName = $"{ownerName}{SnakeToPascal(field["name"]?.GetValue<string>() ?? "Unknown")}";
    var enumMemberRaw = defaultNode.GetValue<string>();
    return $"{enumTypeName}.{MakeEnumMemberName(enumMemberRaw, new HashSet<string>(StringComparer.Ordinal))}";
}

static string ToCSharpStringLiteral(string value)
{
    var escaped = value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    return $"\"{escaped}\"";
}

static string ResolveFieldType(string ownerName, JsonObject field)
{
    var fieldType = field["fieldType"]?.GetValue<string>() ?? "scalar";
    var isArray = field["isArray"]?.GetValue<bool>() ?? false;
    var isOptional = field["isOptional"]?.GetValue<bool>() ?? false;

    var baseType = fieldType switch
    {
        "scalar" => ResolveScalarType(field["scalarType"]?.GetValue<string>() ?? "string"),
        "enum" => $"{ownerName}{SnakeToPascal(field["name"]?.GetValue<string>() ?? "Unknown")}",
        "ref" => field["refStructName"]?.GetValue<string>() ?? "object",
        _ => "object"
    };

    if (isArray)
    {
        baseType = $"IReadOnlyList<{baseType}>";
    }

    if (isOptional)
    {
        if (baseType.EndsWith("?", StringComparison.Ordinal))
        {
            return baseType;
        }

        return $"{baseType}?";
    }

    return baseType;
}

static string ResolveScalarType(string scalarType) => scalarType switch
{
    "int32" => "int",
    "int64" => "long",
    "float32" => "float",
    "float64" => "double",
    "bool" => "bool",
    "boolean" => "bool",
    "string" => "string",
    "bytes" => "byte[]",
    _ => "string"
};

static string SnakeToPascal(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    var builder = new StringBuilder();
    foreach (var rawPart in value.Split('_', StringSplitOptions.RemoveEmptyEntries))
    {
        var part = rawPart.Trim();
        if (part.Length == 0)
        {
            continue;
        }

        builder.Append(char.ToUpperInvariant(part[0]));
        if (part.Length > 1)
        {
            builder.Append(part[1..]);
        }
    }

    return builder.Length == 0 ? "Unknown" : builder.ToString();
}

static string GetNamespace(string area, GeneratorOptions options) => $"{options.RootNamespace}.{area}";

static string GetOutputDirectory(string generatedDir, string area) =>
    Path.Combine([generatedDir, .. area.Split('.')]);

static string MapApiCategoryArea(string categoryName) => categoryName switch
{
    "系统 API" => "System",
    "消息 API" => "Message",
    "好友 API" => "Friend",
    "群聊 API" => "Group",
    "文件 API" => "File",
    _ => "Misc"
};

static void WriteFile(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
}

internal sealed record GenerationContext(
    IReadOnlyDictionary<string, HashSet<string>> UnionMembers,
    IReadOnlyDictionary<string, HashSet<string>> ReferencedInterfaces);

internal sealed record EnumDefinition(string Name, IReadOnlyList<string> Values);
internal sealed record EventFieldMetadataDefinition(string PropertyName, string Description);
internal sealed record EventMetadataDefinition(string TypeName, string Description, IReadOnlyList<EventFieldMetadataDefinition> Fields);
internal sealed record DiscriminatorTypeDefinition(string Discriminator, string TypeName);
internal sealed record IrSourceInfo(
    string Name,
    string Url,
    string Sha256,
    string MilkyVersion,
    string MilkyPackageVersion);
internal sealed record ExistingModelParameter(string Name, string Type);
internal sealed record ExistingAbiMember(IReadOnlyList<ExistingModelParameter> Parameters);
internal sealed record ExistingRecordAbi(
    IReadOnlyList<ExistingModelParameter> PrimaryParameters,
    IReadOnlyList<ExistingAbiMember> Constructors,
    IReadOnlyList<ExistingAbiMember> Deconstructors);
internal sealed record ModelCompatibilityCatalog(
    IReadOnlyDictionary<string, ExistingRecordAbi> Records,
    HashSet<string> PublicTypes);
internal sealed record GeneratedModelParameter(string Name, string Type, JsonObject Field);
internal sealed record CompatibilityAbiMember(
    IReadOnlyList<ExistingModelParameter> Parameters,
    IReadOnlyList<string> PropertyNames);
internal sealed record CompatibilityPlan(
    IReadOnlyList<CompatibilityAbiMember> Constructors,
    IReadOnlyList<CompatibilityAbiMember> Deconstructors)
{
    public static CompatibilityPlan Empty { get; } = new([], []);
}

internal sealed record GeneratorOptions(
    string IrUrl,
    string IrSourceName,
    string OutputDirectory,
    string RootNamespace,
    string? ExpectedSha256,
    bool SelfTest)
{
    private const string DefaultIrUrl = "https://unpkg.com/@saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json";
    private const string DefaultIrSha256 = "17a4f1da0ce44640ab73840015756227b8180ca5a503433ba4d41a3a82a13ea0";

    public string GeneratedRootNamespace => $"{RootNamespace}.Generated";

    public static GeneratorOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";

            values[key] = value;
        }

        var projectRoot = FindProjectRoot();
        var defaultOutput = Path.Combine(projectRoot, "output", "Generated");
        var irUrl = values.GetValueOrDefault("--ir-url") ?? DefaultIrUrl;
        var irSourceName = values.GetValueOrDefault("--ir-source") ?? "@saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json";
        var outputDirectory = values.GetValueOrDefault("--output");
        var rootNamespace = values.GetValueOrDefault("--namespace") ?? "Milky.Models";
        var expectedSha256 = values.GetValueOrDefault("--expected-sha256") ??
                             (values.ContainsKey("--ir-url") ? null : DefaultIrSha256);
        var selfTest = values.ContainsKey("--self-test");

        var resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? defaultOutput
            : Path.GetFullPath(outputDirectory);

        return new GeneratorOptions(
            irUrl,
            irSourceName,
            resolvedOutputDirectory,
            rootNamespace,
            expectedSha256,
            selfTest);
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "MilkyModelGenerator.Net.csproj")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
    }
}
