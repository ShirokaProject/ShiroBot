using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using ShiroBot.SDK.Config;

[assembly: ShiroBotApiCompatibility("0.9.2", "0.9.2")]

#if (useQq)
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.4")]
#endif
#if (useDiscord)
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "0.9.4")]
#endif
#if (useTelegram)
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "0.9.4")]
#endif

namespace PluginTemplate;

[BotPlugin("PluginTemplate", Name = "PluginTemplate", Version = "TemplateVersion", Author = "TemplateAuthor")]
public sealed class Plugin : PluginBase<PluginConfig>
{
    protected override void ConfigureRoutes()
    {
        AllCommands.MapExact("ping", message => Context.Message.ReplyAsync(message, "pong"));
    }

    protected override Task LoadAsync()
    {
        // Settings was loaded by the host before this hook runs.
        return Task.CompletedTask;
    }

    protected override Task OnConfigChangedAsync(
        PluginConfig previous,
        PluginConfig current,
        CancellationToken cancellationToken)
    {
        // The host handles TOML loading and file watching. Apply runtime-specific changes here.
        return Task.CompletedTask;
    }
}

[ConfigModel]
public sealed class PluginConfig
{
    [ConfigField("Whether this plugin responds to commands.", Label = "Enabled", Group = "general", GroupLabel = "General", GroupOrder = 10, Order = 10)]
    public bool Enabled { get; set; } = true;
}
