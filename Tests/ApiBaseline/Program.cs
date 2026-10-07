using System.Reflection;
using System.Text.Json;
using ShiroBot.SDK.Plugin;
using ShiroBot.SDK.Adapter;
using ShiroBot.Model.QQ;
using ShiroBot.Model.Discord;
using ShiroBot.Model.Telegram;

var assemblies = new[] { typeof(IBotContext).Assembly, typeof(IQGroupApi).Assembly, typeof(DiscordUser).Assembly, typeof(TelegramUser).Assembly };
var lines = new SortedSet<string>(StringComparer.Ordinal);
var nullability = new NullabilityInfoContext();
string TypeName(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition().FullName!.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">" : type.FullName ?? type.Name;
string Parameters(MethodBase method) => string.Join(",", method.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? "=" + (p.DefaultValue ?? "null") : "")));
foreach (var assembly in assemblies)
{
    lines.Add("assembly " + assembly.GetName().Name + " " + assembly.GetName().Version);
    foreach (var type in assembly.GetExportedTypes())
    {
        var name = TypeName(type);
        lines.Add("type " + name + " : " + (type.BaseType is null ? "" : TypeName(type.BaseType)) + " implements " + string.Join(",", type.GetInterfaces().Select(TypeName).Order(StringComparer.Ordinal)));
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var ctor in type.GetConstructors(flags)) lines.Add(name + " ctor(" + Parameters(ctor) + ")");
        foreach (var method in type.GetMethods(flags)) lines.Add(name + (method.IsStatic ? " static " : " ") + TypeName(method.ReturnType) + " " + method.Name + "(" + Parameters(method) + ")");
        foreach (var field in type.GetFields(flags)) lines.Add(name + " field " + TypeName(field.FieldType) + " " + field.Name + (field.IsLiteral ? "=" + field.GetRawConstantValue() : ""));
        foreach (var property in type.GetProperties(flags))
        {
            var info = nullability.Create(property);
            lines.Add(name + " property " + TypeName(property.PropertyType) + " " + property.Name + " nullable=" + info.ReadState + "/" + info.WriteState
                + " required=" + property.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute")
                + " init=" + (property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit") == true));
        }
    }
}
// Every asynchronous QQ capability must accept cancellation (DisposeAsync is the inherited lifecycle exception).
foreach (var type in typeof(IQGroupApi).Assembly.GetExportedTypes().Where(t => t.IsInterface).Concat(new[] { typeof(IMessageService), typeof(IChannelService), typeof(IUserService), typeof(IMessageInteractionService), typeof(IMessageReactionService) }))
    foreach (var method in type.GetMethods().Where(m => typeof(Task).IsAssignableFrom(m.ReturnType)))
        if (!method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken))) throw new InvalidOperationException("Missing CancellationToken: " + type.Name + "." + method.Name);
var path = Path.GetFullPath(args.Length > 1 ? args[1] : "Tests/ApiBaseline/PublicApi.json");
var current = lines.ToArray();
if (args.FirstOrDefault() == "--update")
{
    File.WriteAllText(path, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    Console.WriteLine("Public API baseline updated: " + current.Length + " declarations.");
}
else
{
    var expected = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))!;
    var added = current.Except(expected).ToArray(); var removed = expected.Except(current).ToArray();
    if (added.Length + removed.Length != 0) throw new InvalidOperationException("Public API changed. Review ABI and update baseline intentionally.\nAdded:\n" + string.Join("\n", added) + "\nRemoved:\n" + string.Join("\n", removed));
    Console.WriteLine("SDK and all built-in Model public API baseline and QQ cancellation contract verification passed.");
}
