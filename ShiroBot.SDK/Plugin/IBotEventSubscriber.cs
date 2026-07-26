using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public interface IBotEventSubscriber
{
    Task OnEventAsync(BotEvent e);
}
