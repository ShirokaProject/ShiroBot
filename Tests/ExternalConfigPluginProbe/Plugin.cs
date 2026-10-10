using ShiroBot.SDK.Plugin;
using ShiroBot.ExternalConfigModelProbe;
namespace ShiroBot.ExternalConfigPluginProbe;

public abstract class ConfiguredBase<T> : PluginBase<T> where T : class, new();
public sealed class ProbePlugin : ConfiguredBase<Settings>
{
    public override string Name => "ExternalConfigProbe";
}
// The declared contract must win over an unrelated class with the conventional name.
public sealed class PluginConfig { public string WrongField { get; set; } = ""; }
