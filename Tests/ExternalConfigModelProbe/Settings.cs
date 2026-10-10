using ShiroBot.SDK.Config;
namespace ShiroBot.ExternalConfigModelProbe;

// Deliberately not named PluginConfig and not marked ConfigModel.
public sealed class Settings
{
    public static int Constructions;
    public Settings() => Interlocked.Increment(ref Constructions);
    [ConfigField("完整 Cookie，留空禁用。", Label = "登录凭据", Type = "password",
        Group = "cookies", GroupLabel = "Cookie", GroupDescription = "登录设置")]
    public string Cookie { get; set; } = "";
    [ConfigField("重试次数", Label = "重试", Group = "network", GroupLabel = "网络")]
    public int Retries { get; set; } = 5;
}
