using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[assembly: ShiroBotApiCompatibility("1.0", "1.0")]

namespace AdapterTemplate.TestPlugin;

[BotPlugin("AdapterTemplate.TestPlugin", Name = "Adapter test plugin", Version = "1.0.0", Author = "TemplateAuthor")]
public sealed class Plugin : PluginBase
{
    protected override void ConfigureRoutes()
    {
        AllCommands.MapExact("ping", message => Context.Message.ReplyAsync(message, "pong"));
        AllCommands.MapExact("adapter-info", message => Context.Message.ReplyAsync(message,
            $"Platform: {message.Platform}\nInstance: {message.InstanceId}\nChannel: {message.Channel.Type} / {message.Channel.Id}\nSender: {message.Sender.Id}\nMessage: {message.MessageId}\nSegments: {string.Join(", ", message.Segments.Select(segment => segment.GetType().Name))}"));
        AllCommands.MapPrefix("adapter-echo ", message => Context.Message.ReplyAsync(message,
            message.GetPlainText()["adapter-echo ".Length..]));
    }
}
