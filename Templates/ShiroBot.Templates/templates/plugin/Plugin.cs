using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[assembly: ShiroBotApiCompatibility("0.9", "0.9")]

#if (useQq)
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.1")]
#endif
#if (useDiscord)
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "0.9.1")]
#endif
#if (useTelegram)
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "0.9.1")]
#endif

namespace PluginTemplate;

[BotPlugin("PluginTemplate", Name = "PluginTemplate", Version = "TemplateVersion", Author = "TemplateAuthor")]
public sealed class Plugin : PluginBase
{
    protected override void ConfigureRoutes()
    {
        AllCommands.MapExact("ping", message => Context.Message.ReplyAsync(message, "pong"));
    }
}
