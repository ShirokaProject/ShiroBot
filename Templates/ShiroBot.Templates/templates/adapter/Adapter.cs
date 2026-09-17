using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

[assembly: ShiroBotApiCompatibility("0.9", "0.9")]

#if (useQq)
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.2")]
#endif
#if (useDiscord)
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "0.9.2")]
#endif
#if (useTelegram)
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "0.9.2")]
#endif

namespace AdapterTemplate;

[BotAdapter("AdapterTemplate", Name = "AdapterTemplate", Version = "TemplateVersion", Author = "TemplateAuthor")]
public sealed class Adapter : IBotAdapter
{
    private readonly AdapterEventService _events = new();

    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public string Platform => "template-platform";
    public IMessageService Message { get; } = new AdapterMessageService();
    public IChannelService Channel { get; } = new AdapterChannelService();
    public IUserService User { get; } = new AdapterUserService();
    public IEventService Event => _events;

    public Task StartAsync()
    {
        Logger.Info("AdapterTemplate started.");
        // Connect to the platform and publish mapped events through _events.PublishAsync(...).
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Logger.Info("AdapterTemplate stopped.");
        return Task.CompletedTask;
    }
}

internal sealed class AdapterMessageService : IMessageService
{
    public Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments)
    {
        // Map common message segments to the platform API and return its message ID.
        throw new NotImplementedException();
    }
}

internal sealed class AdapterChannelService : IChannelService;

internal sealed class AdapterUserService : IUserService;

internal sealed class AdapterEventService : IEventService
{
    public event Func<BotEvent, Task> EventReceived = delegate { return Task.CompletedTask; };

    public async Task PublishAsync(BotEvent botEvent)
    {
        foreach (var handler in EventReceived.GetInvocationList().Cast<Func<BotEvent, Task>>())
        {
            await handler(botEvent);
        }
    }
}
