using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ShiroBot.SDK.Generators;

internal enum Shape
{
    String, Boolean, Number, Guid, DateTime, DateTimeOffset, Enum, Nullable,
    Array, List, Dictionary, Object, JsonNode, JsonElement
}

/// <summary>契约里一个类型的形状，决定怎么读写和怎么描述成 JSON Schema。</summary>
internal sealed class TypeModel
{
    public TypeModel(Shape kind, ITypeSymbol symbol, bool nullable)
    {
        Kind = kind;
        Symbol = symbol;
        Nullable = nullable;
    }

    public Shape Kind { get; }
    public ITypeSymbol Symbol { get; }
    /// <summary>引用类型标了 ? 或 Nullable&lt;T&gt;：允许 null。</summary>
    public bool Nullable { get; }
    public TypeModel? Element { get; set; }
    /// <summary>数字类型对应的 Utf8JsonReader.GetXxx 后缀，例如 Int32。</summary>
    public string NumberMethod { get; set; } = "";
    public bool Integer { get; set; }

    /// <summary>不带可空标记的完全限定名，用作 C# 类型。</summary>
    public string Display => Symbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>带可空标记的完全限定名。</summary>
    public string DisplayWithNullability => Nullable && Symbol.IsReferenceType ? Display + "?" : Display;

    public string Key => Kind + ":" + Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes))
        + (Nullable ? "?" : "");
}

/// <summary>契约对象类型的一个属性。</summary>
internal sealed class PropertyModel
{
    public PropertyModel(IPropertySymbol symbol, string jsonName, TypeModel type)
    {
        Symbol = symbol;
        JsonName = jsonName;
        Type = type;
    }

    public IPropertySymbol Symbol { get; }
    public string Name => Symbol.Name;
    public string JsonName { get; }
    public TypeModel Type { get; }
    public IParameterSymbol? Parameter { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public bool Settable => Symbol.SetMethod is { } set && !set.IsInitOnly && set.DeclaredAccessibility == Accessibility.Public;
}

internal sealed class ObjectModel
{
    public ObjectModel(INamedTypeSymbol symbol) => Symbol = symbol;

    public INamedTypeSymbol Symbol { get; }
    public List<PropertyModel> Properties { get; } = new();
    public IMethodSymbol? Constructor { get; set; }
    public bool IsRecord => Symbol.IsRecord;
}

/// <summary>把 Roslyn 类型分类成 <see cref="TypeModel"/>；不支持的类型报告诊断。</summary>
internal sealed class TypeCatalog
{
    private readonly Compilation _compilation;
    private readonly Dictionary<string, ObjectModel> _objects = new();
    private readonly Dictionary<string, TypeModel> _types = new();

    public TypeCatalog(Compilation compilation) => _compilation = compilation;

    public List<Diagnostic> Diagnostics { get; } = new();
    public IEnumerable<ObjectModel> Objects => _objects.Values;
    public IEnumerable<TypeModel> Types => _types.Values;
    public HashSet<string> Enums { get; } = new();

    public ObjectModel? ObjectOf(ITypeSymbol symbol) =>
        _objects.TryGetValue(symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var model) ? model : null;

    /// <summary>契约里的根类型：请求、响应、事件和描述。只接受对象类型。</summary>
    public TypeModel? AddRoot(ITypeSymbol symbol, Location? location)
    {
        var model = Classify(symbol, symbol.NullableAnnotation == NullableAnnotation.Annotated, location);
        if (model is not null && model.Kind != Shape.Object)
        {
            Diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.RootNotObject, location, symbol.ToDisplayString()));
            return null;
        }
        return model;
    }

    public TypeModel? Classify(ITypeSymbol symbol, bool nullable, Location? location)
    {
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            var inner = Classify(nullableType.TypeArguments[0], false, location);
            if (inner is null) return null;
            return Remember(new TypeModel(Shape.Nullable, symbol, true) { Element = inner });
        }

        var model = symbol.SpecialType switch
        {
            SpecialType.System_String => new TypeModel(Shape.String, symbol, nullable),
            SpecialType.System_Boolean => new TypeModel(Shape.Boolean, symbol, false),
            SpecialType.System_Byte => Number(symbol, "Byte", true),
            SpecialType.System_SByte => Number(symbol, "SByte", true),
            SpecialType.System_Int16 => Number(symbol, "Int16", true),
            SpecialType.System_UInt16 => Number(symbol, "UInt16", true),
            SpecialType.System_Int32 => Number(symbol, "Int32", true),
            SpecialType.System_UInt32 => Number(symbol, "UInt32", true),
            SpecialType.System_Int64 => Number(symbol, "Int64", true),
            SpecialType.System_UInt64 => Number(symbol, "UInt64", true),
            SpecialType.System_Single => Number(symbol, "Single", false),
            SpecialType.System_Double => Number(symbol, "Double", false),
            SpecialType.System_Decimal => Number(symbol, "Decimal", false),
            SpecialType.System_DateTime => new TypeModel(Shape.DateTime, symbol, false),
            _ => null
        };
        if (model is not null) return Remember(model);

        var fullName = symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (fullName)
        {
            case "global::System.Guid":
                return Remember(new TypeModel(Shape.Guid, symbol, false));
            case "global::System.DateTimeOffset":
                return Remember(new TypeModel(Shape.DateTimeOffset, symbol, false));
            case "global::System.Text.Json.JsonElement":
                return Remember(new TypeModel(Shape.JsonElement, symbol, false));
            case "global::System.Text.Json.Nodes.JsonNode":
            case "global::System.Text.Json.Nodes.JsonObject":
            case "global::System.Text.Json.Nodes.JsonArray":
            case "global::System.Text.Json.Nodes.JsonValue":
                return Remember(new TypeModel(Shape.JsonNode, symbol, nullable));
        }

        if (symbol is IArrayTypeSymbol { Rank: 1 } array)
        {
            var element = Classify(array.ElementType, array.ElementType.NullableAnnotation == NullableAnnotation.Annotated, location);
            return element is null ? null : Remember(new TypeModel(Shape.Array, symbol, nullable) { Element = element });
        }

        if (symbol is INamedTypeSymbol named)
        {
            if (named.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
            {
                Enums.Add(fullName);
                return Remember(new TypeModel(Shape.Enum, symbol, false));
            }

            if (named.IsGenericType)
            {
                switch (fullName)
                {
                    case "global::System.Collections.Generic.List<T>":
                    case "global::System.Collections.Generic.IList<T>":
                    case "global::System.Collections.Generic.IReadOnlyList<T>":
                    case "global::System.Collections.Generic.ICollection<T>":
                    case "global::System.Collections.Generic.IReadOnlyCollection<T>":
                    case "global::System.Collections.Generic.IEnumerable<T>":
                    {
                        var item = named.TypeArguments[0];
                        var element = Classify(item, item.NullableAnnotation == NullableAnnotation.Annotated, location);
                        return element is null ? null : Remember(new TypeModel(Shape.List, symbol, nullable) { Element = element });
                    }
                    case "global::System.Collections.Generic.Dictionary<TKey, TValue>":
                    case "global::System.Collections.Generic.IDictionary<TKey, TValue>":
                    case "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                    {
                        if (named.TypeArguments[0].SpecialType != SpecialType.System_String) break;
                        var value = named.TypeArguments[1];
                        var element = Classify(value, value.NullableAnnotation == NullableAnnotation.Annotated, location);
                        return element is null ? null : Remember(new TypeModel(Shape.Dictionary, symbol, nullable) { Element = element });
                    }
                }
            }
            else if (named.TypeKind is Microsoft.CodeAnalysis.TypeKind.Class or Microsoft.CodeAnalysis.TypeKind.Struct
                     && !named.IsAbstract && !named.IsStatic && !IsFrameworkType(named))
            {
                if (named.ContainingType is not null)
                {
                    Diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.NestedType, location, named.ToDisplayString()));
                    return null;
                }
                var objectModel = new TypeModel(Shape.Object, symbol, nullable && named.IsReferenceType);
                Remember(objectModel);
                AddObject(named, location);
                return objectModel;
            }
        }

        Diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.UnsupportedType, location, symbol.ToDisplayString()));
        return null;
    }

    private void AddObject(INamedTypeSymbol symbol, Location? location)
    {
        var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (_objects.ContainsKey(key)) return;
        var model = new ObjectModel(symbol);
        _objects.Add(key, model);

        var properties = symbol.GetMembers().OfType<IPropertySymbol>()
            .Where(property => !property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public
                               && property.GetMethod is not null && property.Name != "EqualityContract"
                               && !HasAttribute(property, "global::System.Text.Json.Serialization.JsonIgnoreAttribute"))
            .ToList();

        // 位置记录的主构造函数：参数和属性一一对应时用它构造。
        model.Constructor = symbol.InstanceConstructors
            .Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length > 0
                           && ctor.Parameters.All(parameter => properties.Any(property =>
                               string.Equals(property.Name, parameter.Name, System.StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(ctor => ctor.Parameters.Length)
            .FirstOrDefault();
        if (model.Constructor is null && !symbol.InstanceConstructors.Any(ctor =>
                ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public))
        {
            Diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.NoConstructor, location, symbol.ToDisplayString()));
        }

        foreach (var property in properties)
        {
            var parameter = model.Constructor?.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, property.Name, System.StringComparison.OrdinalIgnoreCase));
            var type = Classify(property.Type, property.Type.NullableAnnotation == NullableAnnotation.Annotated,
                property.Locations.FirstOrDefault() ?? location);
            if (type is null) continue;
            if (parameter is null && property.SetMethod is null) continue;   // 只读的计算属性不属于消息
            var jsonName = AttributeString(property, "global::System.Text.Json.Serialization.JsonPropertyNameAttribute")
                           ?? SnakeCase(property.Name);
            model.Properties.Add(new PropertyModel(property, jsonName, type)
            {
                Parameter = parameter,
                Description = AttributeString(property, "global::System.ComponentModel.DescriptionAttribute")
                              ?? (parameter is null ? null : AttributeString(parameter, "global::System.ComponentModel.DescriptionAttribute")),
                Required = property.IsRequired || parameter is { HasExplicitDefaultValue: false } && !type.Nullable
            });
        }
    }

    private TypeModel Remember(TypeModel model)
    {
        if (!_types.ContainsKey(model.Key)) _types.Add(model.Key, model);
        return _types[model.Key];
    }

    private static TypeModel Number(ITypeSymbol symbol, string method, bool integer) =>
        new(Shape.Number, symbol, false) { NumberMethod = method, Integer = integer };

    private static bool IsFrameworkType(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "System" || ns.StartsWith("System.", System.StringComparison.Ordinal)
               || ns.StartsWith("Microsoft.", System.StringComparison.Ordinal);
    }

    public static bool HasAttribute(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullName);

    public static string? AttributeString(ISymbol symbol, string fullName) =>
        symbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullName)
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    /// <summary>与 System.Text.Json 的 SnakeCaseLower 一致。</summary>
    public static string SnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch))
            {
                var previousLower = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextLower = i > 0 && i + 1 < name.Length && char.IsUpper(name[i - 1]) && char.IsLower(name[i + 1]);
                if (previousLower || nextLower) builder.Append('_');
                builder.Append(char.ToLowerInvariant(ch));
            }
            else builder.Append(ch);
        }
        return builder.ToString();
    }
}
