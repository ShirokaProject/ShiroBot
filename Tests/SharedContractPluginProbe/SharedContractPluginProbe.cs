using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using ShiroBot.Model.QQ;

[assembly: ShiroBotApiCompatibility("0.8", "0.8")]

namespace ShiroBot.SharedContractPluginProbe;

[BotPlugin(
    "SharedContractPluginProbe",
    Name = "Shared contract plugin probe",
    Version = "0.9.2",
    SharedAssemblies = "ShiroBot.Model.QQ")]
public sealed class SharedContractPluginProbe : PluginBase
{
    public override string Name => "SharedContractPluginProbe";
    public static Type SharedQqContractType => typeof(QGroup);
}
