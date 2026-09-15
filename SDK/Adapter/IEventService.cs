using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

public interface IEventService
{
    event Func<BotEvent, Task> EventReceived;
}
