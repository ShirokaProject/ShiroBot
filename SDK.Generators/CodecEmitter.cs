using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShiroBot.SDK.Generators;

/// <summary>为契约类型生成基于 Utf8JsonReader / Utf8JsonWriter 的读写代码和 JSON Schema。</summary>
internal sealed class CodecEmitter
{
    private const string Io = "global::ShiroBot.SDK.Plugin.PluginJsonIO";
    private const string Token = "global::System.Text.Json.JsonTokenType";
    private const string Reader = "global::System.Text.Json.Utf8JsonReader";
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";

    private readonly TypeCatalog _catalog;
    private readonly Dictionary<string, int> _methods = new();
    private readonly Dictionary<string, string> _codecs = new();

    public CodecEmitter(TypeCatalog catalog)
    {
        _catalog = catalog;
        var index = 0;
        foreach (var type in catalog.Types) _methods[type.Key] = index++;
        index = 0;
        foreach (var model in catalog.Objects)
            _codecs[model.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = "__PluginJson_" + index++;
    }

    /// <summary>对象类型的读写类名，可以在同一个生成文件里引用 <c>.Instance</c>。</summary>
    public string CodecOf(ITypeSymbol symbol) =>
        _codecs[symbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)];

    public void Emit(StringBuilder output)
    {
        output.AppendLine("file static class __PluginJsonIo");
        output.AppendLine("{");
        foreach (var type in _catalog.Types) EmitMethods(output, type);
        output.AppendLine("}");
        output.AppendLine();
        foreach (var model in _catalog.Objects) EmitCodec(output, model);
    }

    public IEnumerable<string> CodecNames => _catalog.Objects.Select(model => CodecOf(model.Symbol));

    private string Read(TypeModel type) => "__PluginJsonIo.Read_" + _methods[type.Key];
    private string Write(TypeModel type) => "__PluginJsonIo.Write_" + _methods[type.Key];

    private void EmitMethods(StringBuilder o, TypeModel type)
    {
        var id = _methods[type.Key];
        var t = type.DisplayWithNullability;
        o.AppendLine($"    internal static {t} Read_{id}(ref {Reader} r, string f)");
        o.AppendLine("    {");
        switch (type.Kind)
        {
            case Shape.String:
                o.AppendLine($"        return {Io}.ReadString(ref r, f){(type.Nullable ? "" : " ?? \"\"")};");
                break;
            case Shape.Boolean:
                o.AppendLine($"        {Io}.Expect(ref r, {Token}.True, f);");
                o.AppendLine("        return r.GetBoolean();");
                break;
            case Shape.Number:
                o.AppendLine($"        {Io}.Expect(ref r, {Token}.Number, f);");
                o.AppendLine($"        return r.Get{type.NumberMethod}();");
                break;
            case Shape.Guid:
            case Shape.DateTime:
            case Shape.DateTimeOffset:
                o.AppendLine($"        {Io}.Expect(ref r, {Token}.String, f);");
                o.AppendLine($"        return r.Get{type.Kind}();");
                break;
            case Shape.Enum:
                o.AppendLine($"        return {Io}.ReadEnum<{type.Display}>(ref r, f);");
                break;
            case Shape.Nullable:
                o.AppendLine($"        if (r.TokenType == {Token}.Null) return null;");
                o.AppendLine($"        return {Read(type.Element!)}(ref r, f);");
                break;
            case Shape.JsonElement:
                o.AppendLine("        return global::System.Text.Json.JsonElement.ParseValue(ref r);");
                break;
            case Shape.JsonNode:
                o.AppendLine($"        if (r.TokenType == {Token}.Null) return {(type.Nullable ? "null" : "null!")};");
                o.AppendLine($"        return ({t})global::System.Text.Json.Nodes.JsonNode.Parse(ref r){(type.Nullable ? "" : "!")};");
                break;
            case Shape.Array:
            case Shape.List:
            {
                var element = type.Element!;
                o.AppendLine($"        if (!{Io}.StartArray(ref r, f)) return {(type.Nullable ? "null" : EmptyOf(type))};");
                o.AppendLine($"        var list = new global::System.Collections.Generic.List<{element.DisplayWithNullability}>();");
                o.AppendLine($"        while (r.Read() && r.TokenType != {Token}.EndArray) list.Add({Read(element)}(ref r, f));");
                o.AppendLine(type.Kind == Shape.Array ? "        return list.ToArray();" : "        return list;");
                break;
            }
            case Shape.Dictionary:
            {
                var element = type.Element!;
                o.AppendLine($"        if (r.TokenType == {Token}.Null) return {(type.Nullable ? "null" : EmptyOf(type))};");
                o.AppendLine($"        if (!{Io}.StartObject(ref r, f)) return {EmptyOf(type)};");
                o.AppendLine($"        var map = new global::System.Collections.Generic.Dictionary<string, {element.DisplayWithNullability}>();");
                o.AppendLine($"        while (r.Read() && r.TokenType != {Token}.EndObject)");
                o.AppendLine("        {");
                o.AppendLine("            var key = r.GetString()!;");
                o.AppendLine("            r.Read();");
                o.AppendLine($"            map[key] = {Read(element)}(ref r, f + \".\" + key);");
                o.AppendLine("        }");
                o.AppendLine("        return map;");
                break;
            }
            case Shape.Object:
                o.AppendLine($"        return {CodecOf(type.Symbol)}.Instance.Read(ref r);");
                break;
        }
        o.AppendLine("    }");
        o.AppendLine();

        o.AppendLine($"    internal static void Write_{id}({Writer} w, {t} v)");
        o.AppendLine("    {");
        var canBeNull = type.Symbol.IsReferenceType || type.Kind == Shape.Nullable;
        if (canBeNull && type.Kind != Shape.Nullable)
            o.AppendLine("        if (v is null) { w.WriteNullValue(); return; }");
        switch (type.Kind)
        {
            case Shape.String:
            case Shape.Guid:
            case Shape.DateTime:
            case Shape.DateTimeOffset:
                o.AppendLine("        w.WriteStringValue(v);");
                break;
            case Shape.Boolean:
                o.AppendLine("        w.WriteBooleanValue(v);");
                break;
            case Shape.Number:
                o.AppendLine("        w.WriteNumberValue(v);");
                break;
            case Shape.Enum:
                o.AppendLine($"        {Io}.WriteEnum(w, v);");
                break;
            case Shape.Nullable:
                o.AppendLine($"        if (v.HasValue) {Write(type.Element!)}(w, v.Value); else w.WriteNullValue();");
                break;
            case Shape.JsonElement:
            case Shape.JsonNode:
                o.AppendLine("        v.WriteTo(w);");
                break;
            case Shape.Array:
            case Shape.List:
                o.AppendLine("        w.WriteStartArray();");
                o.AppendLine($"        foreach (var item in v) {Write(type.Element!)}(w, item);");
                o.AppendLine("        w.WriteEndArray();");
                break;
            case Shape.Dictionary:
                o.AppendLine("        w.WriteStartObject();");
                o.AppendLine($"        foreach (var pair in v) {{ w.WritePropertyName(pair.Key); {Write(type.Element!)}(w, pair.Value); }}");
                o.AppendLine("        w.WriteEndObject();");
                break;
            case Shape.Object:
                o.AppendLine($"        {CodecOf(type.Symbol)}.Instance.Write(w, v);");
                break;
        }
        o.AppendLine("    }");
        o.AppendLine();
    }

    private void EmitCodec(StringBuilder o, ObjectModel model)
    {
        var name = CodecOf(model.Symbol);
        var t = model.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        o.AppendLine($"file sealed class {name} : global::ShiroBot.SDK.Plugin.PluginJsonType<{t}>");
        o.AppendLine("{");
        o.AppendLine($"    public static readonly {name} Instance = new();");
        o.AppendLine($"    public override string Schema => {SymbolDisplay.FormatLiteral(SchemaOf(model, new HashSet<string>()), true)};");
        o.AppendLine();

        o.AppendLine($"    public override void Write({Writer} w, {t} v)");
        o.AppendLine("    {");
        o.AppendLine("        w.WriteStartObject();");
        for (var i = 0; i < model.Properties.Count; i++)
        {
            var property = model.Properties[i];
            var nameLiteral = SymbolDisplay.FormatLiteral(property.JsonName, true);
            if (property.Type.Symbol.IsReferenceType || property.Type.Kind == Shape.Nullable)
                o.AppendLine($"        {{ var x = v.{property.Name}; if (x is not null) {{ w.WritePropertyName({nameLiteral}u8); {Write(property.Type)}(w, x); }} }}");
            else
                o.AppendLine($"        w.WritePropertyName({nameLiteral}u8); {Write(property.Type)}(w, v.{property.Name});");
        }
        o.AppendLine("        w.WriteEndObject();");
        o.AppendLine("    }");
        o.AppendLine();

        o.AppendLine($"    public override {t} Read(ref {Reader} r)");
        o.AppendLine("    {");
        o.AppendLine($"        if (!{Io}.StartObject(ref r, {SymbolDisplay.FormatLiteral(model.Symbol.Name, true)})) return default!;");
        for (var i = 0; i < model.Properties.Count; i++)
            o.AppendLine($"        {model.Properties[i].Type.DisplayWithNullability} p{i} = default!; var h{i} = false;");
        o.AppendLine($"        while (r.Read() && r.TokenType != {Token}.EndObject)");
        o.AppendLine("        {");
        for (var i = 0; i < model.Properties.Count; i++)
        {
            var property = model.Properties[i];
            var nameLiteral = SymbolDisplay.FormatLiteral(property.JsonName, true);
            o.AppendLine($"            {(i == 0 ? "if" : "else if")} (r.ValueTextEquals({nameLiteral}u8)) {{ r.Read(); p{i} = {Read(property.Type)}(ref r, {nameLiteral}); h{i} = true; }}");
        }
        o.AppendLine($"            {(model.Properties.Count == 0 ? "" : "else ")}{{ r.Read(); r.Skip(); }}");
        o.AppendLine("        }");

        var arguments = model.Constructor?.Parameters
            .Select(parameter =>
            {
                var index = model.Properties.FindIndex(p => p.Parameter is not null && SymbolEqualityComparer.Default.Equals(p.Parameter, parameter));
                return index < 0 ? DefaultFor(parameter, null) : $"h{index} ? p{index} : {DefaultFor(parameter, model.Properties[index].Type)}";
            })
            .ToList() ?? new List<string>();
        var initOnly = new List<string>();
        var later = new List<string>();
        for (var i = 0; i < model.Properties.Count; i++)
        {
            var property = model.Properties[i];
            if (property.Parameter is not null) continue;
            if (model.IsRecord) later.Add($"        if (h{i}) result = result with {{ {property.Name} = p{i} }};");
            else if (property.Settable) later.Add($"        if (h{i}) result.{property.Name} = p{i};");
            else initOnly.Add($"{property.Name} = h{i} ? p{i} : default!");
        }
        o.AppendLine($"        var result = new {t}({string.Join(", ", arguments)}){(initOnly.Count == 0 ? "" : " { " + string.Join(", ", initOnly) + " }")};");
        foreach (var line in later) o.AppendLine(line);
        o.AppendLine("        return result;");
        o.AppendLine("    }");
        o.AppendLine("}");
        o.AppendLine();
    }

    /// <summary>缺少字段时的值：参数默认值；不可空的字符串和集合给空值，其余 default。</summary>
    private static string DefaultFor(IParameterSymbol parameter, TypeModel? type)
    {
        if (parameter.HasExplicitDefaultValue) return Literal(parameter.ExplicitDefaultValue, parameter.Type);
        if (type is null || type.Nullable) return "default!";
        return type.Kind switch
        {
            Shape.String => "\"\"",
            Shape.Array or Shape.List or Shape.Dictionary => EmptyOf(type),
            _ => "default!"
        };
    }

    private static string EmptyOf(TypeModel type) => type.Kind switch
    {
        Shape.Array => $"global::System.Array.Empty<{type.Element!.DisplayWithNullability}>()",
        Shape.List => $"new global::System.Collections.Generic.List<{type.Element!.DisplayWithNullability}>()",
        Shape.Dictionary => $"new global::System.Collections.Generic.Dictionary<string, {type.Element!.DisplayWithNullability}>()",
        _ => "default!"
    };

    public static string Literal(object? value, ITypeSymbol type)
    {
        if (value is null) return type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ? "default" : "null";
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, true),
            char ch => SymbolDisplay.FormatLiteral(ch, true),
            bool flag => flag ? "true" : "false",
            _ when type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum => $"({display}){System.Convert.ToString(value, CultureInfo.InvariantCulture)}",
            _ => $"({display.TrimEnd('?')}){System.Convert.ToString(value, CultureInfo.InvariantCulture)}{(value is decimal ? "m" : value is float ? "f" : "")}"
        };
    }

    // ---- JSON Schema ----

    private string SchemaOf(ObjectModel model, HashSet<string> visiting)
    {
        var key = model.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!visiting.Add(key)) return "{\"type\":\"object\"}";
        var properties = model.Properties.Select(property =>
        {
            var schema = SchemaOf(property.Type, visiting);
            var extra = new List<string>();
            if (property.Description is { Length: > 0 } description) extra.Add("\"description\":" + JsonString(description));
            if (property.Parameter is { HasExplicitDefaultValue: true } parameter && parameter.ExplicitDefaultValue is { } value)
                extra.Add("\"default\":" + JsonValue(value, parameter.Type));
            return JsonString(property.JsonName) + ":" + Merge(extra, schema);
        });
        var required = model.Properties.Where(property => property.Required).Select(property => JsonString(property.JsonName)).ToList();
        visiting.Remove(key);
        return "{\"type\":\"object\",\"properties\":{" + string.Join(",", properties) + "}"
               + (required.Count == 0 ? "" : ",\"required\":[" + string.Join(",", required) + "]") + "}";
    }

    private string SchemaOf(TypeModel type, HashSet<string> visiting) => type.Kind switch
    {
        Shape.String => "{\"type\":\"string\"}",
        Shape.Boolean => "{\"type\":\"boolean\"}",
        Shape.Number => type.Integer ? "{\"type\":\"integer\"}" : "{\"type\":\"number\"}",
        Shape.Guid => "{\"type\":\"string\",\"format\":\"uuid\"}",
        Shape.DateTime or Shape.DateTimeOffset => "{\"type\":\"string\",\"format\":\"date-time\"}",
        Shape.Enum => "{\"type\":\"string\",\"enum\":[" + string.Join(",", ((INamedTypeSymbol)type.Symbol).GetMembers()
            .OfType<IFieldSymbol>().Where(field => field.HasConstantValue)
            .Select(field => JsonString(TypeCatalog.SnakeCase(field.Name)))) + "]}",
        Shape.Nullable => SchemaOf(type.Element!, visiting),
        Shape.Array or Shape.List => "{\"type\":\"array\",\"items\":" + SchemaOf(type.Element!, visiting) + "}",
        Shape.Dictionary => "{\"type\":\"object\",\"additionalProperties\":" + SchemaOf(type.Element!, visiting) + "}",
        Shape.JsonNode => type.Symbol.Name switch { "JsonObject" => "{\"type\":\"object\"}", "JsonArray" => "{\"type\":\"array\"}", _ => "{}" },
        Shape.JsonElement => "{}",
        Shape.Object => _catalog.ObjectOf(type.Symbol) is { } model ? SchemaOf(model, visiting) : "{\"type\":\"object\"}",
        _ => "{}"
    };

    private static string Merge(List<string> fields, string schema)
    {
        if (fields.Count == 0) return schema;
        var body = schema.Substring(1, schema.Length - 2);
        return "{" + string.Join(",", fields) + (body.Length == 0 ? "" : "," + body) + "}";
    }

    private static string JsonValue(object value, ITypeSymbol type)
    {
        if (type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
        {
            var field = ((INamedTypeSymbol)type).GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(member => member.HasConstantValue && Equals(member.ConstantValue, value));
            return field is null ? System.Convert.ToString(value, CultureInfo.InvariantCulture)! : JsonString(TypeCatalog.SnakeCase(field.Name));
        }
        return value switch
        {
            string text => JsonString(text),
            char ch => JsonString(ch.ToString()),
            bool flag => flag ? "true" : "false",
            _ => System.Convert.ToString(value, CultureInfo.InvariantCulture)!
        };
    }

    public static string JsonString(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < 0x20) builder.Append("\\u").Append(((int)ch).ToString("x4"));
                    else builder.Append(ch);
                    break;
            }
        }
        return builder.Append('"').ToString();
    }
}
