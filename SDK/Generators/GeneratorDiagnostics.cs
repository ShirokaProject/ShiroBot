using Microsoft.CodeAnalysis;

namespace ShiroBot.SDK.Generators;

internal static class GeneratorDiagnostics
{
    private const string Category = "ShiroBot.PluginContract";

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "SBC001", "不支持的契约类型",
        "{0} 不能用在插件契约里：只支持 record/类、枚举、字符串、数字、布尔、Guid、日期、集合、字典和 JsonNode",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ContractClass = new(
        "SBC002", "契约类必须是 static partial class",
        "[PluginContract] 类 {0} 必须是顶层的 static partial class",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ContractProperty = new(
        "SBC003", "契约属性写法不正确",
        "{0} 必须是只有 get 的 static partial 属性，类型为 PluginService、PluginServiceFamily 或 PluginEvent，并与特性匹配",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor RootNotObject = new(
        "SBC004", "消息必须是 record 或类",
        "契约的请求、响应、事件和描述必须是 record 或类，{0} 不是",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NestedType = new(
        "SBC005", "契约类型不能嵌套",
        "{0} 声明在其他类型里面；契约类型要声明在命名空间下",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NoConstructor = new(
        "SBC006", "契约类型无法构造",
        "{0} 需要一个参数都对应属性的公开构造函数（例如位置记录），或一个公开的无参构造函数",
        Category, DiagnosticSeverity.Error, true);
}
